using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Compiles a whole mapping graph into a single delegate: nested object maps are inlined and
    /// collections become typed loops, so mapping a graph costs one delegate call instead of one
    /// engine round-trip per object. Anything unusual at runtime (a derived instance, a member the
    /// compiler cannot prove equivalent) is handed to the interpreted engine at the correct depth,
    /// so observable behavior matches the engine.
    /// </summary>
    internal static class GraphCompiler
    {
        private const int MaxInlineHeight = 16;

        private static readonly MethodInfo GetTypeMethod = typeof(object).GetMethod(nameof(GetType));
        private static readonly MethodInfo DepthExceededMethod =
            typeof(FastObjectPlan).GetMethod(nameof(FastObjectPlan.DepthExceeded), BindingFlags.NonPublic | BindingFlags.Static);

        public static void TryCompile(FastObjectPlan plan, TypeMap typeMap, IGlobalConfiguration configuration)
        {
            try
            {
                var source = Expression.Parameter(typeof(object), "source");
                var owner = Expression.Parameter(typeof(Mapper), "owner");
                var context = Expression.Parameter(typeof(ResolutionContext).MakeByRefType(), "context");
                var depth = Expression.Parameter(typeof(int), "depth");

                var state = new State(owner, context, configuration);
                var typedSource = Expression.Variable(typeMap.SourceType, "s0");
                var body = BuildObject(state, typeMap, typedSource, depth, new HashSet<TypeMap>(), 1, out var height);
                if (body == null) return;

                var block = Expression.Block(typeof(object), new[] { typedSource },
                    Expression.Assign(typedSource, Expression.Convert(source, typeMap.SourceType)),
                    Expression.Convert(body, typeof(object)));

                plan.Height = height;
                plan.Pure = Expression.Lambda<PureMapDelegate>(block, source, owner, context, depth).Compile();
            }
            catch
            {
                plan.Pure = null;   // keep the step-based plan
            }
        }

        private sealed class State
        {
            public State(ParameterExpression owner, ParameterExpression context, IGlobalConfiguration configuration)
            {
                Owner = owner;
                Context = context;
                Configuration = configuration;
                ConfigurationConstant = Expression.Constant(configuration, typeof(IGlobalConfiguration));
            }

            public ParameterExpression Owner { get; }
            public ParameterExpression Context { get; }
            public IGlobalConfiguration Configuration { get; }
            public Expression ConfigurationConstant { get; }
            private int _counter;
            public string Name(string prefix) => prefix + (_counter++);
        }

        /// <summary>
        /// Builds an expression creating and populating the destination for <paramref name="typeMap"/>
        /// from a non-null <paramref name="source"/>. <paramref name="depth"/> is this object's level.
        /// Returns null when the map cannot be inlined.
        /// </summary>
        private static Expression BuildObject(State state, TypeMap typeMap, ParameterExpression source, Expression depth,
            HashSet<TypeMap> active, int level, out int height)
        {
            height = level;
            if (level > MaxInlineHeight || active.Contains(typeMap)) return null;
            if (!FastPlanCompiler.IsMapEligible(typeMap)) return null;

            active.Add(typeMap);
            try
            {
                var dest = Expression.Variable(typeMap.DestinationType, state.Name("d"));
                var statements = new List<Expression> { Expression.Assign(dest, Expression.New(typeMap.DestinationType)) };

                if (typeMap.BeforeMapActions.Count > 0)
                    statements.Add(CallActions(state, depth, typeMap, source, dest, before: true));

                foreach (var pm in typeMap.OrderedPropertyMaps)
                {
                    Expression statement = null;
                    if (FastPlanCompiler.IsPlainMember(pm))
                    {
                        statement = FastPlanCompiler.TryBuildLeafAssignment(source, dest, pm);
                        if (statement == null)
                        {
                            statement = TryBuildStructuredMember(state, typeMap, pm, source, dest, depth, active, level, out var childHeight);
                            if (statement != null && childHeight > height) height = childHeight;
                        }
                    }

                    statements.Add(statement ?? CallMapMember(state, depth, source, dest, pm));
                }

                if (typeMap.ActivePathMaps.Length > 0)
                    statements.Add(Expression.Call(PureFallback.MapPathsMethod, state.Owner, state.Context, depth,
                        state.ConfigurationConstant, Expression.Constant(typeMap), Box(source), Box(dest)));

                if (typeMap.AfterMapActions.Count > 0)
                    statements.Add(CallActions(state, depth, typeMap, source, dest, before: false));

                statements.Add(dest);
                return Expression.Block(typeMap.DestinationType, new[] { dest }, statements);
            }
            finally
            {
                active.Remove(typeMap);
            }
        }

        private static Expression Box(Expression e) => e.Type == typeof(object) ? e : Expression.Convert(e, typeof(object));

        /// <summary>Reference cast that also works between unrelated static types (via object).</summary>
        private static Expression Cast(Expression e, Type type) =>
            type.IsAssignableFrom(e.Type) || e.Type.IsAssignableFrom(type)
                ? Expression.Convert(e, type)
                : Expression.Convert(Box(e), type);

        private static Expression RuntimeType(Expression e) => Expression.Call(Box(e), GetTypeMethod);

        private static Expression CallMapMember(State state, Expression depth, Expression source, Expression dest, PropertyMap pm) =>
            Expression.Call(PureFallback.MapMemberMethod, state.Owner, state.Context, depth, state.ConfigurationConstant,
                Box(source), Box(dest), Expression.Constant(pm));

        private static Expression CallActions(State state, Expression depth, TypeMap typeMap, Expression source, Expression dest, bool before) =>
            Expression.Call(PureFallback.RunActionsMethod, state.Owner, state.Context, depth, Expression.Constant(typeMap),
                Box(source), Box(dest), Expression.Constant(before));

        /// <summary>Engine fallback for a value, counted as a child of the level at <paramref name="parentDepth"/>.</summary>
        private static Expression CallEngine(State state, Expression parentDepth, Expression value, Type destinationType, PropertyMap pm) =>
            Expression.Convert(
                Expression.Call(PureFallback.MapThroughEngineMethod, state.Owner, state.Context, parentDepth, Box(value),
                    Expression.Constant(destinationType, typeof(Type)), state.ConfigurationConstant,
                    Expression.Constant(pm, typeof(PropertyMap))),
                destinationType);

        /// <summary>Nested object or collection member; null when not inlinable (member then uses the engine).</summary>
        private static Expression TryBuildStructuredMember(State state, TypeMap typeMap, PropertyMap pm, Expression source,
            Expression dest, Expression depth, HashSet<TypeMap> active, int level, out int height)
        {
            height = level;
            var chain = pm.SourceMembers;
            var sourceMemberType = ReflectionHelper.GetMemberType(chain[chain.Length - 1]);
            var destMember = pm.DestinationMember;
            var destMemberType = ReflectionHelper.GetMemberType(destMember);
            if (sourceMemberType == null || destMemberType == null) return null;

            var target = FastPlanCompiler.Access(dest, destMember);
            if (target == null) return null;

            Expression nullValue;
            Func<Expression, Expression> onValue;
            if (ReflectionHelper.IsCollection(sourceMemberType) && !sourceMemberType.IsDictionaryType()
                && ReflectionHelper.IsCollection(destMemberType) && !destMemberType.IsDictionaryType())
            {
                // Null source collection: null when AllowNullCollections, else an empty collection (§5 step 8).
                var profile = pm.TypeMap?.Profile ?? typeMap.Profile;
                if (profile == null) return null;
                nullValue = profile.AllowsNullCollectionsFor(pm)
                    ? Expression.Default(destMemberType)
                    : NewEmptyCollection(destMemberType);
                if (nullValue == null) return null;
                onValue = BuildCollectionValue(state, pm, sourceMemberType, destMemberType, target, nullValue, depth, active, level, out height);
            }
            else if (IsInlinableObject(sourceMemberType) && IsInlinableObject(destMemberType))
            {
                nullValue = Expression.Default(destMemberType);
                onValue = BuildObjectValue(state, pm, sourceMemberType, destMemberType, target, nullValue, depth, active, level, out height);
            }
            else return null;

            if (onValue == null) return null;
            return FastPlanCompiler.BuildChain(source, chain, 0, onValue, Expression.Assign(target, nullValue), typeof(void));
        }

        private static bool IsInlinableObject(Type t) =>
            !t.IsValueType && MappingEngine.IsComplex(t)
            && !ReflectionHelper.IsCollection(t) && !t.IsDictionaryType() && !MappingEngine.IsDynamic(t);

        /// <summary>
        /// Maps a non-null value of static type <paramref name="sourceType"/> exactly like the engine:
        /// same runtime type as the destination -> reference copy; runtime type == sourceType ->
        /// inlined child map; anything else -> engine.
        /// </summary>
        private static Expression MapReferenceValue(State state, ParameterExpression value, Type sourceType, Type destType,
            Expression inlined, ParameterExpression typed, Expression parentDepth, PropertyMap pm)
        {
            Expression viaEngine = CallEngine(state, parentDepth, value, destType, pm);
            var canBeDestType = sourceType.IsAssignableFrom(destType);   // only then can runtime type equal destType

            if (inlined == null)
            {
                if (!canBeDestType) return viaEngine;
                return Expression.Condition(Expression.Equal(RuntimeType(value), Expression.Constant(destType, typeof(Type))),
                    Cast(value, destType), viaEngine, destType);
            }

            var inlinedBlock = Expression.Block(destType, new[] { typed }, Expression.Assign(typed, value), inlined);

            // A sealed static type means the runtime type is exactly sourceType: no check needed.
            if (sourceType.IsSealed) return inlinedBlock;

            var runtimeType = Expression.Variable(typeof(Type), state.Name("t"));
            Expression mapped = Expression.Condition(
                Expression.Equal(runtimeType, Expression.Constant(sourceType, typeof(Type))), inlinedBlock, viaEngine, destType);
            if (canBeDestType)
                mapped = Expression.Condition(Expression.Equal(runtimeType, Expression.Constant(destType, typeof(Type))),
                    Cast(value, destType), mapped, destType);

            return Expression.Block(destType, new[] { runtimeType }, Expression.Assign(runtimeType, RuntimeType(value)), mapped);
        }

        private static Func<Expression, Expression> BuildObjectValue(State state, PropertyMap pm, Type sourceType, Type destType,
            Expression target, Expression nullValue, Expression depth, HashSet<TypeMap> active, int level, out int height)
        {
            height = level;
            Expression inlined = null;
            var typed = Expression.Variable(sourceType, state.Name("n"));
            if (sourceType != destType)
            {
                var childMap = state.Configuration.FindTypeMapFor(sourceType, destType);
                if (childMap != null && childMap.SourceType == sourceType && childMap.DestinationType == destType)
                {
                    inlined = BuildObject(state, childMap, typed, Expression.Add(depth, Expression.Constant(1)), active, level + 1, out var h);
                    if (inlined != null) height = h;
                }
            }

            return value =>
            {
                var v = Expression.Variable(sourceType, state.Name("v"));
                return Expression.Block(typeof(void), new[] { v },
                    Expression.Assign(v, value),
                    Expression.IfThenElse(
                        Expression.ReferenceEqual(v, Expression.Constant(null, sourceType)),
                        Expression.Assign(target, nullValue),
                        Expression.Assign(target, MapReferenceValue(state, v, sourceType, destType, inlined, typed, depth, pm))));
            };
        }

        /// <summary>Collection member as a typed loop, with the same depth and budget accounting as the engine.</summary>
        private static Func<Expression, Expression> BuildCollectionValue(State state, PropertyMap pm, Type sourceType, Type destType,
            Expression target, Expression nullValue, Expression depth, HashSet<TypeMap> active, int level, out int height)
        {
            // The collection itself is one level; its elements are one deeper (as in the engine).
            height = level + 1;
            if (sourceType.IsValueType) return null;

            var sourceElement = ReflectionHelper.GetEnumerableElementType(sourceType);
            if (sourceElement == null) return null;
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(sourceElement);
            if (!enumerableType.IsAssignableFrom(sourceType)) return null;

            Type destElement;
            bool isArray;
            if (destType.IsArray)
            {
                if (destType.GetArrayRank() != 1) return null;
                destElement = destType.GetElementType();
                isArray = true;
            }
            else if (destType.IsGenericType)
            {
                var def = destType.GetGenericTypeDefinition();
                if (def != typeof(List<>) && def != typeof(IEnumerable<>) && def != typeof(ICollection<>)
                    && def != typeof(IList<>) && def != typeof(IReadOnlyList<>) && def != typeof(IReadOnlyCollection<>))
                    return null;
                destElement = destType.GetGenericArguments()[0];
                isArray = false;
            }
            else return null;

            // A map configured for the collection pair itself would take precedence in the engine.
            if (state.Configuration.FindTypeMapFor(sourceType, destType) != null) return null;

            var collectionDepth = Expression.Add(depth, Expression.Constant(1));
            var elementDepth = Expression.Add(depth, Expression.Constant(2));

            // Element conversion: same type, lossless leaf coercion, or an (inlined) child map.
            Func<Expression, Expression> mapElement;
            if (sourceElement == destElement)
            {
                mapElement = e => e;
            }
            else if (FastPlanCompiler.IsLeaf(sourceElement) && FastPlanCompiler.IsLeaf(destElement)
                     && Nullable.GetUnderlyingType(sourceElement) == null
                     && FastPlanCompiler.Coerce(Expression.Default(sourceElement), destElement) != null)
            {
                // Non-nullable leaf elements only: a null element must keep the engine's behavior.
                mapElement = e => FastPlanCompiler.Coerce(e, destElement);
            }
            else if (IsInlinableObject(sourceElement) && IsInlinableObject(destElement))
            {
                var childMap = state.Configuration.FindTypeMapFor(sourceElement, destElement);
                Expression inlined = null;
                var typed = Expression.Variable(sourceElement, state.Name("e"));
                if (childMap != null && childMap.SourceType == sourceElement && childMap.DestinationType == destElement)
                {
                    inlined = BuildObject(state, childMap, typed, elementDepth, active, level + 2, out var h);
                    if (inlined != null && h > height) height = h;
                }

                mapElement = e =>
                {
                    var item = Expression.Variable(sourceElement, state.Name("i"));
                    return Expression.Block(destElement, new[] { item },
                        Expression.Assign(item, e),
                        Expression.Condition(
                            Expression.ReferenceEqual(item, Expression.Constant(null, sourceElement)),
                            Expression.Default(destElement),
                            MapReferenceValue(state, item, sourceElement, destElement, inlined, typed, collectionDepth, null),
                            destElement));
                };
            }
            else
            {
                return null;   // e.g. element enum/numeric narrowing: the engine handles it
            }

            var listType = typeof(List<>).MakeGenericType(destElement);
            var budget = state.Configuration.MaxCollectionItems;
            var maxDepth = state.Configuration.MaxGraphDepth;

            return value =>
            {
                var v = Expression.Variable(sourceType, state.Name("c"));
                var list = Expression.Variable(listType, state.Name("l"));
                var budgetError = Expression.Throw(Expression.Call(PureFallback.BudgetExceededMethod, Expression.Constant(budget),
                    RuntimeType(v), Expression.Constant(destType, typeof(Type))));

                var fill = BuildFill(state, sourceType, sourceElement, v, list, listType, mapElement, budget, budgetError);

                Expression result = isArray ? (Expression)Expression.Call(list, listType.GetMethod("ToArray")) : list;
                if (result.Type != destType) result = Expression.Convert(result, destType);

                // The collection level must not exceed the depth cap (S1), as in the engine.
                var depthCheck = Expression.IfThen(
                    Expression.GreaterThan(collectionDepth, Expression.Constant(maxDepth)),
                    Expression.Throw(Expression.Call(DepthExceededMethod, Expression.Constant(maxDepth),
                        RuntimeType(v), Expression.Constant(destType, typeof(Type)))));

                Expression mapCollection = Expression.Block(
                    depthCheck,
                    fill,
                    Expression.Assign(target, result));

                // Runtime value of the destination type is copied by reference (engine short-circuit).
                if (sourceType.IsAssignableFrom(destType))
                    mapCollection = Expression.IfThenElse(
                        Expression.Equal(RuntimeType(v), Expression.Constant(destType, typeof(Type))),
                        Expression.Assign(target, Cast(v, destType)),
                        mapCollection);

                return Expression.Block(typeof(void), new[] { v, list },
                    Expression.Assign(v, value),
                    Expression.IfThenElse(
                        Expression.ReferenceEqual(v, Expression.Constant(null, sourceType)),
                        Expression.Assign(target, nullValue),
                        mapCollection));
            };
        }

        /// <summary>Creates the destination list and fills it, using indexed loops for arrays and List&lt;T&gt;.</summary>
        private static Expression BuildFill(State state, Type sourceType, Type sourceElement, ParameterExpression v,
            ParameterExpression list, Type listType, Func<Expression, Expression> mapElement, int budget, Expression budgetError)
        {
            var add = listType.GetMethod("Add");
            var listCtor = listType.GetConstructor(new[] { typeof(int) });
            var index = Expression.Variable(typeof(int), state.Name("k"));
            var length = Expression.Variable(typeof(int), state.Name("len"));
            var breakLabel = Expression.Label(state.Name("done"));

            Expression IndexedLoop(Expression count, Func<Expression, Expression> elementAt) =>
                Expression.Block(new[] { index, length },
                    Expression.Assign(length, count),
                    Expression.Assign(list, Expression.New(listCtor, length)),
                    Expression.Assign(index, Expression.Constant(0)),
                    Expression.Loop(
                        Expression.IfThenElse(
                            Expression.LessThan(index, length),
                            Expression.Block(
                                Expression.IfThen(Expression.GreaterThanOrEqual(index, Expression.Constant(budget)), budgetError),
                                Expression.Call(list, add, mapElement(elementAt(index))),
                                Expression.PreIncrementAssign(index)),
                            Expression.Break(breakLabel)),
                        breakLabel));

            if (sourceType.IsArray && sourceType.GetArrayRank() == 1)
                return IndexedLoop(Expression.ArrayLength(v), i => Expression.ArrayIndex(v, i));

            var sourceListType = typeof(List<>).MakeGenericType(sourceElement);
            if (sourceType == sourceListType)
                return IndexedLoop(Expression.Property(v, "Count"), i => Expression.Property(v, "Item", i));

            // General IEnumerable<T>.
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(sourceElement);
            var enumeratorType = typeof(IEnumerator<>).MakeGenericType(sourceElement);
            var enumerator = Expression.Variable(enumeratorType, state.Name("it"));
            var count = Expression.Variable(typeof(int), state.Name("n"));
            var collectionType = typeof(ICollection<>).MakeGenericType(sourceElement);
            var capacity = collectionType.IsAssignableFrom(sourceType)
                ? (Expression)Expression.Property(Expression.Convert(v, collectionType), "Count")
                : Expression.Constant(0);

            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")),
                    Expression.Block(
                        Expression.IfThen(Expression.GreaterThan(Expression.PreIncrementAssign(count), Expression.Constant(budget)), budgetError),
                        Expression.Call(list, add, mapElement(Expression.Property(enumerator, "Current")))),
                    Expression.Break(breakLabel)),
                breakLabel);

            return Expression.Block(new[] { enumerator, count },
                Expression.Assign(list, Expression.New(listCtor, capacity)),
                Expression.Assign(count, Expression.Constant(0)),
                Expression.Assign(enumerator, Expression.Call(Expression.Convert(v, enumerableType), enumerableType.GetMethod("GetEnumerator"))),
                Expression.TryFinally(loop, Expression.Call(enumerator, typeof(IDisposable).GetMethod("Dispose"))));
        }

        private static Expression NewEmptyCollection(Type destType)
        {
            if (destType.IsArray)
                return Expression.NewArrayBounds(destType.GetElementType(), Expression.Constant(0));
            if (!destType.IsGenericType) return null;
            var listType = typeof(List<>).MakeGenericType(destType.GetGenericArguments()[0]);
            if (!destType.IsAssignableFrom(listType)) return null;
            Expression list = Expression.New(listType);
            return list.Type == destType ? list : Expression.Convert(list, destType);
        }
    }
}
