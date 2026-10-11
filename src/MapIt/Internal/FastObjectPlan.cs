using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A whole-object compiled mapping plan. Construction and all "leaf" member assignments
    /// (including flattened member chains with null propagation) are compiled into delegates;
    /// nested objects and collections call child plans directly instead of re-entering the
    /// interpreted engine. Members the plan cannot prove equivalent fall back to the interpreted
    /// <see cref="MappingEngine"/> member pipeline, so behavior is preserved.
    /// </summary>
    internal sealed class FastObjectPlan
    {
        // Creates the destination; when possible it also performs the leading leaf assignments
        // in the same compiled delegate (one call per object for flat DTOs).
        private readonly Func<object, object> _ctor;
        private readonly FastStep[] _steps;
        private readonly TypeMap _typeMap;
        private readonly IGlobalConfiguration _configuration;
        private readonly bool _hasBefore;
        private readonly bool _hasAfter;
        private readonly PathMap[] _paths;
        private readonly int _maxDepth;

        /// <summary>
        /// Whole-graph compiled delegate (nested maps inlined, typed loops). Null when the graph
        /// contains anything that needs the step-based or interpreted pipeline.
        /// </summary>
        internal PureMapDelegate Pure;

        /// <summary>Maximum number of mapping levels the inlined graph can reach (for the S1 depth cap).</summary>
        internal int Height;

        /// <summary>Whether the inlined delegate can run at <paramref name="depth"/> without crossing MaxGraphDepth.</summary>
        public bool CanRunPure(int depth) => Pure != null && depth + Height - 1 <= _maxDepth;

        internal FastObjectPlan(Func<object, object> ctor, FastStep[] steps, TypeMap typeMap, IGlobalConfiguration configuration)
        {
            _ctor = ctor;
            _steps = steps;
            _typeMap = typeMap;
            _configuration = configuration;
            _hasBefore = typeMap.BeforeMapActions.Count > 0;
            _hasAfter = typeMap.AfterMapActions.Count > 0;
            _paths = typeMap.ActivePathMaps;
            _maxDepth = configuration.MaxGraphDepth;
        }

        /// <summary>Maps a non-null source. Depth for this level is tracked by the caller.</summary>
        public object Execute(object source, ResolutionContext context)
        {
            var dest = _ctor(source);
            if (_hasBefore)
                MappingEngine.RunActions(_typeMap.BeforeMapActions, source, dest, context, _typeMap.SourceType, _typeMap.DestinationType);

            var steps = _steps;
            for (var i = 0; i < steps.Length; i++)
                steps[i].Execute(source, dest, context);

            if (_paths.Length > 0)
                for (var i = 0; i < _paths.Length; i++)
                    MappingEngine.MapPath(_configuration, source, dest, _paths[i], context);

            if (_hasAfter)
                MappingEngine.RunActions(_typeMap.AfterMapActions, source, dest, context, _typeMap.SourceType, _typeMap.DestinationType);
            return dest;
        }

        /// <summary>Maps a nested, non-null source, tracking depth exactly like the engine does (S1).</summary>
        public object ExecuteNested(object source, ResolutionContext context)
        {
            var depth = context.CurrentDepth + 1;
            if (CanRunPure(depth))
            {
                var ctx = context;
                return Pure(source, null, ref ctx, depth);
            }

            context.CurrentDepth++;   // step-based plan: count this level like the engine
            try
            {
                if (context.CurrentDepth > _maxDepth)
                    throw DepthExceeded(_maxDepth, _typeMap.SourceType, _typeMap.DestinationType);
                return Execute(source, context);
            }
            finally
            {
                context.CurrentDepth--;
            }
        }

        internal static MapItMappingException DepthExceeded(int max, Type sourceType, Type destinationType) =>
            new MapItMappingException(
                $"Maximum mapping depth of {max} exceeded mapping {sourceType?.Name} to {destinationType?.Name}. This usually indicates a self-referential graph without a MaxDepth or PreserveReferences setting.")
            { Types = new TypePair(sourceType, destinationType) };
    }

    /// <summary>
    /// Whole-graph compiled mapping. <paramref name="context"/> may be null on entry from the top of
    /// <see cref="Mapper"/>; it is created on first fallback and then shared for the rest of the
    /// call (so the instance cache and depth stay consistent). <paramref name="owner"/> creates it.
    /// <paramref name="depth"/> is the mapping level of <paramref name="source"/> (top level = 1).
    /// </summary>
    internal delegate object PureMapDelegate(object source, Mapper owner, ref ResolutionContext context, int depth);

    /// <summary>Engine fallbacks invoked from inlined plans (rare paths only).</summary>
    internal static class PureFallback
    {
        internal static readonly MethodInfo MapThroughEngineMethod =
            typeof(PureFallback).GetMethod(nameof(MapThroughEngine), BindingFlags.Public | BindingFlags.Static);

        internal static readonly MethodInfo BudgetExceededMethod =
            typeof(MappingEngine).GetMethod(nameof(MappingEngine.CollectionBudgetExceeded), BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly MethodInfo MapMemberMethod =
            typeof(PureFallback).GetMethod(nameof(MapMember), BindingFlags.Public | BindingFlags.Static);

        internal static readonly MethodInfo RunActionsMethod =
            typeof(PureFallback).GetMethod(nameof(RunActions), BindingFlags.Public | BindingFlags.Static);

        internal static readonly MethodInfo MapPathsMethod =
            typeof(PureFallback).GetMethod(nameof(MapPaths), BindingFlags.Public | BindingFlags.Static);

        /// <summary>Runs one member through the interpreted member pipeline at the object's level.</summary>
        public static void MapMember(Mapper owner, ref ResolutionContext context, int depth, IGlobalConfiguration configuration,
            object source, object destination, PropertyMap memberMap)
        {
            if (context == null) context = owner.CreateFastContext();
            var saved = context.CurrentDepth;
            context.CurrentDepth = depth;
            try { MappingEngine.MapProperty(configuration, source, destination, memberMap, context); }
            finally { context.CurrentDepth = saved; }
        }

        /// <summary>Runs a map's before- or after-map actions at the object's level.</summary>
        public static void RunActions(Mapper owner, ref ResolutionContext context, int depth, TypeMap typeMap,
            object source, object destination, bool before)
        {
            if (context == null) context = owner.CreateFastContext();
            var saved = context.CurrentDepth;
            context.CurrentDepth = depth;
            try
            {
                MappingEngine.RunActions(before ? typeMap.BeforeMapActions : typeMap.AfterMapActions,
                    source, destination, context, typeMap.SourceType, typeMap.DestinationType);
            }
            finally { context.CurrentDepth = saved; }
        }

        /// <summary>Runs a map's path maps at the object's level.</summary>
        public static void MapPaths(Mapper owner, ref ResolutionContext context, int depth, IGlobalConfiguration configuration,
            TypeMap typeMap, object source, object destination)
        {
            if (context == null) context = owner.CreateFastContext();
            var saved = context.CurrentDepth;
            context.CurrentDepth = depth;
            try
            {
                foreach (var path in typeMap.ActivePathMaps)
                    MappingEngine.MapPath(configuration, source, destination, path, context);
            }
            finally { context.CurrentDepth = saved; }
        }

        /// <summary>
        /// Maps <paramref name="value"/> through the engine as a child of a level at
        /// <paramref name="parentDepth"/> (the engine then counts the child level itself).
        /// </summary>
        public static object MapThroughEngine(Mapper owner, ref ResolutionContext context, int parentDepth, object value,
            Type destinationType, IGlobalConfiguration configuration, PropertyMap memberMap)
        {
            if (context == null) context = owner.CreateFastContext();
            var saved = context.CurrentDepth;
            context.CurrentDepth = parentDepth;
            try
            {
                var valueType = value.GetType();
                var pair = new TypePair(valueType, destinationType);
                return MappingEngine.Map(configuration, value, null, valueType, destinationType, context, new MapRequest(pair, pair, memberMap));
            }
            finally
            {
                context.CurrentDepth = saved;
            }
        }
    }

    /// <summary>One step of a compiled plan.</summary>
    internal abstract class FastStep
    {
        public abstract void Execute(object source, object destination, ResolutionContext context);
    }

    /// <summary>A fused block of compiled leaf assignments.</summary>
    internal sealed class LeafBlockStep : FastStep
    {
        private readonly Action<object, object> _assign;
        public LeafBlockStep(Action<object, object> assign) => _assign = assign;
        public override void Execute(object source, object destination, ResolutionContext context) => _assign(source, destination);
    }

    /// <summary>Delegates a member to the interpreted member pipeline (exact original semantics).</summary>
    internal sealed class FallbackStep : FastStep
    {
        private readonly IGlobalConfiguration _configuration;
        private readonly PropertyMap _propertyMap;

        public FallbackStep(IGlobalConfiguration configuration, PropertyMap propertyMap)
        {
            _configuration = configuration;
            _propertyMap = propertyMap;
        }

        public override void Execute(object source, object destination, ResolutionContext context)
            => MappingEngine.MapProperty(_configuration, source, destination, _propertyMap, context);
    }

    /// <summary>Lazily resolves a child plan (handles maps declared in any order).</summary>
    internal sealed class PlanRef
    {
        private readonly TypeMap _typeMap;
        private readonly IGlobalConfiguration _configuration;
        private FastObjectPlan _plan;
        private volatile bool _resolved;

        public PlanRef(TypeMap typeMap, IGlobalConfiguration configuration)
        {
            _typeMap = typeMap;
            _configuration = configuration;
        }

        public FastObjectPlan Get()
        {
            if (!_resolved)
            {
                _plan = FastPlanCompiler.GetPlan(_typeMap, _configuration);
                _resolved = true;
            }
            return _plan;
        }
    }

    /// <summary>A nested complex member mapped through its child plan.</summary>
    internal sealed class ComplexStep : FastStep
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;
        private readonly Type _expectedSourceType;
        private readonly Type _destinationMemberType;
        private readonly object _nullValue;
        private readonly PlanRef _child;
        private readonly IGlobalConfiguration _configuration;
        private readonly PropertyMap _propertyMap;

        public ComplexStep(Func<object, object> getter, Action<object, object> setter, Type expectedSourceType,
            Type destinationMemberType, PlanRef child, IGlobalConfiguration configuration, PropertyMap propertyMap)
        {
            _getter = getter;
            _setter = setter;
            _expectedSourceType = expectedSourceType;
            _destinationMemberType = destinationMemberType;
            _nullValue = destinationMemberType.IsValueType && Nullable.GetUnderlyingType(destinationMemberType) == null
                ? Activator.CreateInstance(destinationMemberType)
                : null;
            _child = child;
            _configuration = configuration;
            _propertyMap = propertyMap;
        }

        public override void Execute(object source, object destination, ResolutionContext context)
        {
            var value = _getter(source);
            if (value == null)
            {
                _setter(destination, _nullValue);
                return;
            }

            var valueType = value.GetType();
            if (valueType == _destinationMemberType)
            {
                _setter(destination, value);   // same-type short-circuit, as in the engine
                return;
            }

            if (valueType == _expectedSourceType && _child != null)
            {
                var plan = _child.Get();
                if (plan != null)
                {
                    _setter(destination, plan.ExecuteNested(value, context));
                    return;
                }
            }

            var pair = new TypePair(valueType, _destinationMemberType);
            _setter(destination, MappingEngine.Map(_configuration, value, null, valueType, _destinationMemberType, context,
                new MapRequest(pair, pair, _propertyMap)));
        }
    }

    /// <summary>A collection member mapped element-by-element through the element's child plan.</summary>
    internal sealed class CollectionStep : FastStep
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;
        private readonly Type _destinationMemberType;
        private readonly Type _sourceElementType;
        private readonly Type _destinationElementType;
        private readonly Func<int, IList> _newList;
        private readonly Func<IList, object> _toArray;
        private readonly bool _allowNullCollections;
        private readonly PlanRef _child;
        private readonly IGlobalConfiguration _configuration;
        private readonly int _budget;
        private readonly int _maxDepth;

        public CollectionStep(Func<object, object> getter, Action<object, object> setter, Type destinationMemberType,
            Type sourceElementType, Type destinationElementType, Func<int, IList> newList, Func<IList, object> toArray,
            bool allowNullCollections, PlanRef child, IGlobalConfiguration configuration)
        {
            _getter = getter;
            _setter = setter;
            _destinationMemberType = destinationMemberType;
            _sourceElementType = sourceElementType;
            _destinationElementType = destinationElementType;
            _newList = newList;
            _toArray = toArray;
            _allowNullCollections = allowNullCollections;
            _child = child;
            _configuration = configuration;
            _budget = configuration.MaxCollectionItems;
            _maxDepth = configuration.MaxGraphDepth;
        }

        public override void Execute(object source, object destination, ResolutionContext context)
        {
            var value = _getter(source);
            if (value == null)
            {
                _setter(destination, _allowNullCollections ? null : Materialize(_newList(0)));
                return;
            }

            var valueType = value.GetType();
            if (valueType == _destinationMemberType)
            {
                _setter(destination, value);   // same-type short-circuit, as in the engine
                return;
            }

            // The engine counts one depth level for the collection itself (S1).
            context.CurrentDepth++;
            try
            {
                if (context.CurrentDepth > _maxDepth)
                    throw FastObjectPlan.DepthExceeded(_maxDepth, valueType, _destinationMemberType);

                var capacity = value is ICollection c ? c.Count : 0;
                var list = _newList(capacity);
                var plan = _child?.Get();
                var count = 0;
                foreach (var item in (IEnumerable)value)
                {
                    if (++count > _budget)
                        throw MappingEngine.CollectionBudgetExceeded(_budget, valueType, _destinationMemberType);
                    list.Add(MapElement(item, plan, context));
                }
                _setter(destination, Materialize(list));
            }
            finally
            {
                context.CurrentDepth--;
            }
        }

        private object Materialize(IList list) => _toArray != null ? _toArray(list) : list;

        private object MapElement(object item, FastObjectPlan plan, ResolutionContext context)
        {
            if (item == null) return null;
            var itemType = item.GetType();
            if (itemType == _destinationElementType) return item;
            if (plan != null && itemType == _sourceElementType) return plan.ExecuteNested(item, context);
            var pair = new TypePair(itemType, _destinationElementType);
            return MappingEngine.Map(_configuration, item, null, itemType, _destinationElementType, context,
                new MapRequest(pair, pair, null));
        }
    }

    /// <summary>Builds and caches <see cref="FastObjectPlan"/>s per type map.</summary>
    internal static class FastPlanCompiler
    {
        /// <summary>
        /// When true, no compiled plans are built and every map uses the interpreted engine.
        /// Set only by the test suite to check that both paths behave identically.
        /// </summary>
        internal static bool DisableCompiledPlans { get; set; } =
            Environment.GetEnvironmentVariable("MAPIT_DISABLE_COMPILED_PLANS") == "1";

        public static FastObjectPlan GetPlan(TypeMap typeMap, IGlobalConfiguration configuration)
        {
            if (DisableCompiledPlans) return null;
            if (typeMap.FastPlanState == 2) return typeMap.FastPlan;
            lock (typeMap.FastPlanLock)
            {
                if (typeMap.FastPlanState == 2) return typeMap.FastPlan;
                FastObjectPlan plan;
                try { plan = TryBuild(typeMap, configuration); }
                catch { plan = null; }   // any build problem simply means "use the interpreted engine"
                if (plan != null)
                    GraphCompiler.TryCompile(plan, typeMap, configuration);   // whole-graph delegate when possible
                typeMap.FastPlan = plan;
                typeMap.FastPlanState = 2;
                return plan;
            }
        }

        // ---- Eligibility -------------------------------------------------------------------

        internal static bool IsMapEligible(TypeMap tm)
        {
            if (!tm.IsSealed || tm.IsOpenGeneric) return false;
            var src = tm.SourceType;
            var dst = tm.DestinationType;
            if (src == null || dst == null || src == dst) return false;
            if (tm.TypeConverter != null || tm.CustomCtor != null) return false;
            if (tm.ConstructorParameterMaps.Count > 0 || tm.ConstructViaServiceLocator) return false;
            if (tm.PreserveReferences || tm.MaxDepth != int.MaxValue) return false;
            if (tm.IncludedDerivedTypes.Count > 0 || tm.IncludeAllDerived) return false;
            if (tm.DestinationTypeOverride != null) return false;
            if (tm.Profile == null || !tm.Profile.AllowNullDestinationValues) return false;

            if (!dst.IsClass || dst.IsAbstract || dst.ContainsGenericParameters) return false;
            if (dst.GetConstructor(Type.EmptyTypes) == null) return false;
            if (!IsPlainObject(src) || !IsPlainObject(dst)) return false;

            // A user conversion operator between the two types would win in the engine.
            if (MappingEngine.FindConversionOperator(ReflectionHelper.GetUnderlyingNullableType(src),
                    ReflectionHelper.GetUnderlyingNullableType(dst)) != null) return false;
            return true;
        }

        /// <summary>A complex type the engine maps member-by-member (not collection/dictionary/dynamic).</summary>
        internal static bool IsPlainObject(Type t) =>
            MappingEngine.IsComplex(t)
            && !ReflectionHelper.IsCollection(t)
            && !t.IsDictionaryType()
            && !MappingEngine.IsDynamic(t);

        /// <summary>A member with no configuration that would change the plain pipeline.</summary>
        internal static bool IsPlainMember(PropertyMap pm)
        {
            if (pm.Ignored || !pm.CanBeSet || pm.DestinationMember == null) return false;
            if (pm.Resolver != null || pm.AsyncResolver != null) return false;
            if (pm.Condition != null || pm.ConditionDelegate != null) return false;
            if (pm.PreCondition != null || pm.PreConditionDelegate != null) return false;
            if (pm.NullSubstitute != null) return false;
            if (pm.UseDestinationValue == true) return false;
            if (pm.ValueTransformers != null && pm.ValueTransformers.Count > 0) return false;
            if (pm.AllowNull.HasValue) return false;
            if (pm.SourceMembers == null || pm.SourceMembers.Length == 0) return false;
            return true;
        }

        internal static bool IsLeaf(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.IsPrimitive || t.IsEnum) return true;
            return t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid);
        }

        // ---- Build -------------------------------------------------------------------------

        private static FastObjectPlan TryBuild(TypeMap tm, IGlobalConfiguration configuration)
        {
            if (!IsMapEligible(tm)) return null;

            var steps = new List<FastStep>();
            var srcParam = Expression.Parameter(typeof(object), "s");
            var dstParam = Expression.Parameter(typeof(object), "d");
            var srcTyped = Expression.Variable(tm.SourceType, "src");
            var dstTyped = Expression.Variable(tm.DestinationType, "dst");
            var pendingLeaves = new List<Expression>();

            // The leading leaf block can be fused into construction, unless before-map actions
            // must observe the freshly constructed (still unmapped) destination.
            var canFuseIntoCtor = tm.BeforeMapActions.Count == 0;
            List<Expression> ctorLeaves = null;

            void FlushLeaves()
            {
                if (pendingLeaves.Count == 0) return;
                if (canFuseIntoCtor && steps.Count == 0 && ctorLeaves == null)
                {
                    ctorLeaves = new List<Expression>(pendingLeaves);
                    pendingLeaves.Clear();
                    return;
                }
                var body = new List<Expression>
                {
                    Expression.Assign(srcTyped, Expression.Convert(srcParam, tm.SourceType)),
                    Expression.Assign(dstTyped, Expression.Convert(dstParam, tm.DestinationType))
                };
                body.AddRange(pendingLeaves);
                var block = Expression.Block(typeof(void), new[] { srcTyped, dstTyped }, body);
                steps.Add(new LeafBlockStep(Expression.Lambda<Action<object, object>>(block, srcParam, dstParam).Compile()));
                pendingLeaves.Clear();
            }

            foreach (var pm in tm.OrderedPropertyMaps)
            {
                if (IsPlainMember(pm))
                {
                    var leaf = TryBuildLeafAssignment(srcTyped, dstTyped, pm);
                    if (leaf != null)
                    {
                        pendingLeaves.Add(leaf);
                        continue;
                    }

                    var structured = TryBuildStructuredStep(tm, pm, configuration);
                    if (structured != null)
                    {
                        FlushLeaves();
                        steps.Add(structured);
                        continue;
                    }
                }

                FlushLeaves();
                steps.Add(new FallbackStep(configuration, pm));
            }
            FlushLeaves();

            // Constructor delegate: new TDestination() plus any fused leading leaf assignments.
            var ctorBody = new List<Expression> { Expression.Assign(dstTyped, Expression.New(tm.DestinationType)) };
            if (ctorLeaves != null)
            {
                ctorBody.Add(Expression.Assign(srcTyped, Expression.Convert(srcParam, tm.SourceType)));
                ctorBody.AddRange(ctorLeaves);
            }
            ctorBody.Add(Expression.Convert(dstTyped, typeof(object)));
            var ctor = Expression.Lambda<Func<object, object>>(
                Expression.Block(typeof(object), new[] { srcTyped, dstTyped }, ctorBody), srcParam).Compile();

            return new FastObjectPlan(ctor, steps.ToArray(), tm, configuration);
        }

        /// <summary>
        /// Compiles <c>dst.Member = coerce(src.A.B...)</c> for leaf types, with engine-equivalent
        /// null propagation (a null intermediate assigns the member's default). Returns null when
        /// the member is not a provably-equivalent leaf assignment.
        /// </summary>
        internal static Expression TryBuildLeafAssignment(ParameterExpression srcTyped, ParameterExpression dstTyped, PropertyMap pm)
        {
            var chain = pm.SourceMembers;
            var finalType = ReflectionHelper.GetMemberType(chain[chain.Length - 1]);
            var destMember = pm.DestinationMember;
            var destType = ReflectionHelper.GetMemberType(destMember);
            if (finalType == null || destType == null) return null;
            if (!IsLeaf(finalType) || !IsLeaf(destType)) return null;
            if (Coerce(Expression.Default(finalType), destType) == null) return null;

            var target = Access(dstTyped, destMember);
            if (target == null) return null;
            var onNull = Expression.Assign(target, Expression.Default(destType));

            return BuildChain(srcTyped, chain, 0, v =>
            {
                var coerced = Coerce(v, destType);
                return coerced == null ? null : Expression.Assign(target, coerced);
            }, onNull, typeof(void));
        }

        /// <summary>Builds a nested-object or collection step, or null when not applicable.</summary>
        private static FastStep TryBuildStructuredStep(TypeMap tm, PropertyMap pm, IGlobalConfiguration configuration)
        {
            var chain = pm.SourceMembers;
            var sourceMemberType = ReflectionHelper.GetMemberType(chain[chain.Length - 1]);
            var destMember = pm.DestinationMember;
            var destMemberType = ReflectionHelper.GetMemberType(destMember);
            if (sourceMemberType == null || destMemberType == null) return null;

            var setter = ReflectionHelper.GetCompiledSetter(destMember);
            if (setter == null) return null;

            // Collection member.
            if (ReflectionHelper.IsCollection(sourceMemberType) && !sourceMemberType.IsDictionaryType()
                && ReflectionHelper.IsCollection(destMemberType) && !destMemberType.IsDictionaryType())
            {
                return TryBuildCollectionStep(tm, pm, chain, sourceMemberType, destMemberType, setter, configuration);
            }

            // Nested object member.
            if (IsPlainObject(sourceMemberType) && IsPlainObject(destMemberType))
            {
                var getter = BuildChainGetter(tm.SourceType, chain);
                if (getter == null) return null;
                PlanRef child = null;
                if (sourceMemberType != destMemberType)
                {
                    var childMap = configuration.FindTypeMapFor(sourceMemberType, destMemberType);
                    if (childMap != null) child = new PlanRef(childMap, configuration);
                }
                return new ComplexStep(getter, setter, sourceMemberType, destMemberType, child, configuration, pm);
            }

            return null;
        }

        private static FastStep TryBuildCollectionStep(TypeMap tm, PropertyMap pm, MemberInfo[] chain,
            Type sourceMemberType, Type destMemberType, Action<object, object> setter, IGlobalConfiguration configuration)
        {
            var sourceElement = ReflectionHelper.GetEnumerableElementType(sourceMemberType);
            if (sourceElement == null) return null;

            // Supported destination shapes: T[], List<T>, and interfaces List<T> satisfies.
            Type destElement;
            bool isArray;
            if (destMemberType.IsArray)
            {
                if (destMemberType.GetArrayRank() != 1) return null;
                destElement = destMemberType.GetElementType();
                isArray = true;
            }
            else if (destMemberType.IsGenericType)
            {
                var def = destMemberType.GetGenericTypeDefinition();
                if (def != typeof(List<>) && def != typeof(IEnumerable<>) && def != typeof(ICollection<>)
                    && def != typeof(IList<>) && def != typeof(IReadOnlyList<>) && def != typeof(IReadOnlyCollection<>))
                    return null;
                destElement = destMemberType.GetGenericArguments()[0];
                isArray = false;
            }
            else return null;

            // A map configured for the collection pair itself would take precedence in the engine.
            if (configuration.FindTypeMapFor(sourceMemberType, destMemberType) != null) return null;

            var getter = BuildChainGetter(tm.SourceType, chain);
            if (getter == null) return null;

            var listType = typeof(List<>).MakeGenericType(destElement);
            var capacity = Expression.Parameter(typeof(int), "capacity");
            var newList = Expression.Lambda<Func<int, IList>>(
                Expression.Convert(Expression.New(listType.GetConstructor(new[] { typeof(int) }), capacity), typeof(IList)),
                capacity).Compile();

            Func<IList, object> toArray = null;
            if (isArray)
            {
                var l = Expression.Parameter(typeof(IList), "l");
                var call = Expression.Call(Expression.Convert(l, listType), listType.GetMethod("ToArray", Type.EmptyTypes));
                toArray = Expression.Lambda<Func<IList, object>>(Expression.Convert(call, typeof(object)), l).Compile();
            }

            var profile = pm.TypeMap?.Profile ?? tm.Profile;
            if (profile == null) return null;
            var allowNullCollections = profile.AllowsNullCollectionsFor(pm);

            PlanRef child = null;
            if (sourceElement != destElement && IsPlainObject(sourceElement) && IsPlainObject(destElement))
            {
                var childMap = configuration.FindTypeMapFor(sourceElement, destElement);
                if (childMap != null) child = new PlanRef(childMap, configuration);
            }

            return new CollectionStep(getter, setter, destMemberType, sourceElement, destElement, newList, toArray,
                allowNullCollections, child, configuration);
        }

        /// <summary>Compiles a source member chain getter returning the boxed value, or null on a null intermediate.</summary>
        private static Func<object, object> BuildChainGetter(Type sourceType, MemberInfo[] chain)
        {
            var p = Expression.Parameter(typeof(object), "s");
            var typed = Expression.Variable(sourceType, "src");
            var nullObj = Expression.Constant(null, typeof(object));
            var chainExpr = BuildChain(typed, chain, 0, v => Expression.Convert(v, typeof(object)), nullObj, typeof(object));
            if (chainExpr == null) return null;
            var body = Expression.Block(typeof(object), new[] { typed },
                Expression.Assign(typed, Expression.Convert(p, sourceType)), chainExpr);
            return Expression.Lambda<Func<object, object>>(body, p).Compile();
        }

        /// <summary>
        /// Walks a member chain, evaluating each member once and short-circuiting to
        /// <paramref name="onNull"/> on a null reference intermediate (matches the engine's chain walk).
        /// </summary>
        internal static Expression BuildChain(Expression current, MemberInfo[] chain, int index,
            Func<Expression, Expression> onValue, Expression onNull, Type resultType)
        {
            var access = Access(current, chain[index]);
            if (access == null) return null;
            if (index == chain.Length - 1) return onValue(access);

            var type = access.Type;
            if (Nullable.GetUnderlyingType(type) != null) return null;   // keep Nullable<T> intermediates on the engine

            var tmp = Expression.Variable(type, "c" + index);
            var rest = BuildChain(tmp, chain, index + 1, onValue, onNull, resultType);
            if (rest == null) return null;

            Expression body;
            if (!type.IsValueType)
            {
                var isNull = Expression.ReferenceEqual(tmp, Expression.Constant(null, type));
                body = resultType == typeof(void)
                    ? (Expression)Expression.IfThenElse(isNull, onNull, rest)
                    : Expression.Condition(isNull, onNull, rest, resultType);
            }
            else
            {
                body = rest;
            }
            return Expression.Block(resultType, new[] { tmp }, Expression.Assign(tmp, access), body);
        }

        /// <summary>Member access for properties, fields, parameterless instance methods and extension methods.</summary>
        internal static Expression Access(Expression target, MemberInfo member)
        {
            switch (member)
            {
                case PropertyInfo p:
                    if (p.GetIndexParameters().Length > 0 || p.GetGetMethod(true) == null) return null;
                    return Expression.Property(EnsureType(target, p.DeclaringType), p);
                case FieldInfo f:
                    return Expression.Field(EnsureType(target, f.DeclaringType), f);
                case MethodInfo m:
                    if (m.IsStatic)
                    {
                        var ps = m.GetParameters();
                        if (ps.Length != 1) return null;
                        return Expression.Call(m, EnsureType(target, ps[0].ParameterType));
                    }
                    if (m.GetParameters().Length != 0) return null;
                    return Expression.Call(EnsureType(target, m.DeclaringType), m);
                default:
                    return null;
            }
        }

        private static Expression EnsureType(Expression target, Type type) =>
            type == null || type == target.Type ? target : Expression.Convert(target, type);

        /// <summary>
        /// Converts a leaf value to the destination type only where the result is identical to the
        /// engine's conversion: same type, T&lt;-&gt;Nullable&lt;T&gt;, and lossless numeric widening.
        /// </summary>
        internal static Expression Coerce(Expression value, Type destType)
        {
            var srcType = value.Type;
            if (srcType == destType) return value;

            var su = Nullable.GetUnderlyingType(srcType);
            var du = Nullable.GetUnderlyingType(destType);
            if (su != null && du == null && su == destType)
                return Expression.Call(value, srcType.GetMethod("GetValueOrDefault", Type.EmptyTypes));
            if (du != null && su == null && du == srcType)
                return Expression.Convert(value, destType);
            if (su == null && du == null && IsWidening(srcType, destType))
                return Expression.Convert(value, destType);
            return null;
        }

        private static readonly Dictionary<Type, Type[]> Widening = new Dictionary<Type, Type[]>
        {
            [typeof(sbyte)] = new[] { typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) },
            [typeof(byte)] = new[] { typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(short)] = new[] { typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) },
            [typeof(ushort)] = new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(int)] = new[] { typeof(long), typeof(float), typeof(double), typeof(decimal) },
            [typeof(uint)] = new[] { typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(long)] = new[] { typeof(float), typeof(double), typeof(decimal) },
            [typeof(ulong)] = new[] { typeof(float), typeof(double), typeof(decimal) },
            [typeof(float)] = new[] { typeof(double) },
            [typeof(char)] = new[] { typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
        };

        private static bool IsWidening(Type from, Type to) =>
            Widening.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
    }
}
