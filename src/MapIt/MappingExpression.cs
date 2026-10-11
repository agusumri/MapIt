using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A mapping expression for a map with untyped source/destination.
    /// </summary>
    public class MappingExpression :
        IMappingExpression,
        IMappingExpressionBase<object, object, IMappingExpression>,
        IProjectionExpression<object, object, IMappingExpression>
    {
        internal MappingExpression(TypeMap typeMap, Profile profile)
        {
            TypeMap = typeMap;
            Profile = profile;
        }

        internal TypeMap TypeMap { get; }
        internal Profile Profile { get; }

        #region IMappingExpression

        /// <inheritdoc />
        public IMappingExpression ForMember(string name, Action<IMemberConfigurationExpression> memberOptions)
        {
            if (TypeMap.IsOpenGeneric)
            {
                var rec = new RecordingMemberConfig();
                memberOptions(rec);
                TypeMap.OpenGenericTypeMap.Steps.Add(new OpenMemberStep { DestinationMemberName = name, Recipe = rec.Recipe });
                return this;
            }
            var prop = TypeMap.DestinationType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) throw new ArgumentException($"Destination {TypeMap.DestinationType.Name} has no property '{name}'.");
            var memberMap = TypeMap.FindOrCreatePropertyMapFor(prop, prop.PropertyType);
            var memberConfig = new MemberConfigurationExpression(TypeMap, Profile, memberMap, prop);
            memberOptions(memberConfig);
            return this;
        }

        /// <inheritdoc />
        public void ForAllMembers(Action<IMemberConfigurationExpression> memberOptions)
        {
            foreach (var prop in TypeMap.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                var memberMap = TypeMap.FindOrCreatePropertyMapFor(prop, prop.PropertyType);
                var memberConfig = new MemberConfigurationExpression(TypeMap, Profile, memberMap, prop);
                memberOptions(memberConfig);
            }
        }

        /// <inheritdoc />
        public IMappingExpression IncludeMembers(string[] memberNames)
        {
            if (memberNames != null && TypeMap.SourceType != null)
            {
                foreach (var name in memberNames)
                {
                    var member = ReflectionHelper.GetFieldOrProperty(TypeMap.SourceType, name);
                    if (member != null) TypeMap.IncludedMemberChains.Add(new[] { member });
                }
            }
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression ReverseMap()
        {
            var reverseMap = new TypeMap(TypeMap.DestinationType, TypeMap.SourceType, MemberList.Destination);
            if (reverseMap.IsOpenGeneric)
                reverseMap.OpenGenericTypeMap = new OpenGenericTypeMap(TypeMap.DestinationType, TypeMap.SourceType);
            reverseMap.ReverseOf = TypeMap;
            Profile.TypeMaps.Add(reverseMap);
            return new MappingExpression(reverseMap, Profile);
        }

        #endregion

        #region IMappingExpressionBase<object, object, IMappingExpression>

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.ConstructUsing(Func<object, ResolutionContext, object> ctor) => this;

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.ConstructUsingServiceLocator() => this;

        /// <inheritdoc />
        public IMappingExpression DisableCtorValidation()
        {
            TypeMap.DisableConstructorValidation = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression BeforeMap(Action<object, object> beforeFunction)
        {
            TypeMap.AddBeforeMapAction(new MapAction { Action = (s, d, ctx) => beforeFunction(s, d) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression BeforeMap(Action<object, object, ResolutionContext> beforeFunction)
        {
            TypeMap.AddBeforeMapAction(new MapAction { Action = beforeFunction });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression BeforeMap<TMappingAction>() where TMappingAction : IMappingAction<object, object>
        {
            TypeMap.AddBeforeMapAction(new MapAction { Action = (s, d, ctx) => MapIt.Internal.ServiceFactory.Create<TMappingAction>(ctx).Process((object)s, (object)d, ctx) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression AfterMap(Action<object, object> afterFunction)
        {
            TypeMap.AddAfterMapAction(new MapAction { Action = (s, d, ctx) => afterFunction(s, d) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression AfterMap(Action<object, object, ResolutionContext> afterFunction)
        {
            TypeMap.AddAfterMapAction(new MapAction { Action = afterFunction });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression AfterMap<TMappingAction>() where TMappingAction : IMappingAction<object, object>
        {
            TypeMap.AddAfterMapAction(new MapAction { Action = (s, d, ctx) => MapIt.Internal.ServiceFactory.Create<TMappingAction>(ctx).Process((object)s, (object)d, ctx) });
            return this;
        }

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.AsProxy() => this;

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.Include(Type derivedSourceType, Type derivedDestinationType)
        {
            if (TypeMap.IsOpenGeneric)
                TypeMap.OpenGenericTypeMap.IncludedDerived.Add((derivedSourceType, derivedDestinationType));
            else
                TypeMap.IncludedDerivedTypes.Add(new TypePair(derivedSourceType, derivedDestinationType));
            return this;
        }

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.IncludeAllDerived()
        {
            TypeMap.IncludeAllDerived = true;
            if (TypeMap.OpenGenericTypeMap != null) TypeMap.OpenGenericTypeMap.IncludeAllDerived = true;
            return this;
        }

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.IncludeBase(Type sourceBase, Type destinationBase)
        {
            if (TypeMap.IsOpenGeneric)
                TypeMap.OpenGenericTypeMap.IncludedBase.Add((sourceBase, destinationBase));
            else
                TypeMap.IncludedBaseTypes.Add(new TypePair(sourceBase, destinationBase));
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression MaxDepth(int depth)
        {
            TypeMap.MaxDepth = depth;
            TypeMap.HasExplicitMaxDepth = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression PreserveReferences()
        {
            TypeMap.PreserveReferences = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression ValidateMemberList(MemberList memberList)
        {
            TypeMap.ConfiguredMemberList = memberList;
            return this;
        }

        /// <inheritdoc />
        void IMappingExpressionBase<object, object, IMappingExpression>.As(Type typeOverride)
        {
            if (typeOverride == null) return;
            if (TypeMap.IsOpenGeneric && typeOverride.IsGenericTypeDefinition)
                TypeMap.OpenGenericTypeMap.DestinationTypeOverrideDefinition = typeOverride;
            else
                TypeMap.DestinationTypeOverride = typeOverride;
        }

        /// <inheritdoc />
        public void ConvertUsing(ITypeConverter<object, object> converter)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => converter.Convert((object)s, (object)d, ctx) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Func<object, object> mappingFunction)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => mappingFunction(s) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Func<object, object, object> mappingFunction)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => mappingFunction((object)s, (object)d) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Func<object, object, ResolutionContext, object> mappingFunction)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => mappingFunction((object)s, (object)d, ctx) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Type typeConverterType)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) =>
            {
                var converter = (ITypeConverter<object, object>)MapIt.Internal.ServiceFactory.Create(typeConverterType, ctx);
                return converter.Convert(s, d, ctx);
            } };
        }

        /// <inheritdoc />
        public void ConvertUsing<TTypeConverter>() where TTypeConverter : ITypeConverter<object, object>
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => MapIt.Internal.ServiceFactory.Create<TTypeConverter>(ctx).Convert(s, d, ctx) };
        }

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.ForCtorParam(string ctorParamName, Action<ICtorParamConfigurationExpression<object>> paramOptions)
        {
            if (TypeMap.IsOpenGeneric)
            {
                var rec = new RecordingCtorParamConfig();
                paramOptions(rec);
                TypeMap.OpenGenericTypeMap.Steps.Add(new OpenCtorParamStep { CtorParamName = ctorParamName, Recipe = rec.Recipe });
                return this;
            }
            var cfg = new CtorParamConfig(TypeMap, ctorParamName);
            paramOptions(cfg);
            return this;
        }

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.ForSourceMember(string sourceMemberName, Action<ISourceMemberConfigurationExpression> memberOptions)
        {
            if (!TypeMap.IsOpenGeneric)
            {
                var member = ReflectionHelper.GetFieldOrProperty(TypeMap.SourceType, sourceMemberName);
                if (member != null)
                {
                    var cfg = TypeMap.FindOrCreateSourceMemberConfigFor(member);
                    memberOptions(new SourceMemberConfigExpression(cfg));
                }
            }
            return this;
        }

        /// <inheritdoc />
        IMappingExpression IMappingExpressionBase<object, object, IMappingExpression>.ConstructUsing(Expression<Func<object, object>> ctor) => this;

        /// <inheritdoc />
        IMappingExpression IProjectionExpressionBase<object, object, IMappingExpression>.ConstructUsing(Expression<Func<object, object>> ctor)
        {
            var func = ctor.Compile();
            TypeMap.CustomCtor = new CustomCtor { Ctor = (s, ctx) => func.DynamicInvoke(s) };
            return this;
        }

        /// <inheritdoc />
        IMappingExpression IProjectionExpressionBase<object, object, IMappingExpression>.ForCtorParam(string ctorParamName, Action<ICtorParamConfigurationExpression<object>> paramOptions) => this;

        /// <inheritdoc />
        void IProjectionExpressionBase<object, object, IMappingExpression>.ConvertUsing(Expression<Func<object, object>> mappingExpression)
        {
            var func = mappingExpression.Compile();
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => func.DynamicInvoke(s) };
        }

        #endregion

        #region IProjectionExpression<object, object, IMappingExpression>

        /// <inheritdoc />
        IMappingExpression IProjectionExpression<object, object, IMappingExpression>.AddTransform<TValue>(Expression<Func<TValue, TValue>> transformer) => this;

        /// <inheritdoc />
        IMappingExpression IProjectionExpression<object, object, IMappingExpression>.IncludeMembers(Expression<Func<object, object>>[] memberExpressions)
        {
            if (memberExpressions != null)
            {
                foreach (var expr in memberExpressions)
                {
                    var chain = MemberConfigurationExpression<object, object, object>.GetMemberPath(expr);
                    if (chain != null && chain.Length > 0)
                        TypeMap.IncludedMemberChains.Add(chain);
                }
            }
            return this;
        }

        #endregion
    }

    /// <summary>
    /// A mapping expression for a map with known source and destination types.
    /// </summary>
    public class MappingExpression<TSource, TDestination> :
        IMappingExpression<TSource, TDestination>,
        IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>,
        IProjectionExpression<TSource, TDestination, IMappingExpression<TSource, TDestination>>
    {
        internal MappingExpression(TypeMap typeMap, Profile profile)
        {
            TypeMap = typeMap;
            Profile = profile;
        }

        internal TypeMap TypeMap { get; }
        internal Profile Profile { get; }

        #region IMappingExpression<TSource, TDestination>

        /// <inheritdoc />
        public IMappingExpression<TDestination, TSource> ReverseMap()
        {
            var reverseMap = new TypeMap(typeof(TDestination), typeof(TSource), MemberList.Destination);
            reverseMap.ReverseOf = TypeMap;
            Profile.TypeMaps.Add(reverseMap);
            return new MappingExpression<TDestination, TSource>(reverseMap, Profile);
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> ForMember(string name, Action<IMemberConfigurationExpression<TSource, TDestination, object>> memberOptions)
        {
            if (TypeMap.IsOpenGeneric)
            {
                var rec = new RecordingMemberConfig();
                memberOptions((IMemberConfigurationExpression<TSource, TDestination, object>)(object)new TypedRecordingMemberConfig<TSource, TDestination, object>(rec));
                TypeMap.OpenGenericTypeMap.Steps.Add(new OpenMemberStep { DestinationMemberName = name, Recipe = rec.Recipe });
                return this;
            }
            var prop = TypeMap.DestinationType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) throw new ArgumentException($"Destination {TypeMap.DestinationType.Name} has no property '{name}'.");
            var memberMap = TypeMap.FindOrCreatePropertyMapFor(prop, prop.PropertyType);
            var memberConfig = new MemberConfigurationExpression<TSource, TDestination, object>(TypeMap, Profile, memberMap, prop);
            memberOptions(memberConfig);
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> memberOptions)
        {
            if (TypeMap.IsOpenGeneric)
            {
                var rec = new RecordingMemberConfig();
                memberOptions(new TypedRecordingMemberConfig<TSource, TDestination, TMember>(rec));
                var name = GetMemberNameFromExpression(destinationMember);
                TypeMap.OpenGenericTypeMap.Steps.Add(new OpenMemberStep { DestinationMemberName = name, Recipe = rec.Recipe });
                return this;
            }
            var member = GetMemberFromExpression(destinationMember);
            if (member == null) throw new ArgumentException("Invalid member expression.");
            var memberMap = TypeMap.FindOrCreatePropertyMapFor(member, ReflectionHelper.GetMemberType(member));
            var memberConfig = new MemberConfigurationExpression<TSource, TDestination, TMember>(TypeMap, Profile, memberMap, member);
            memberOptions(memberConfig);
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> ForPath<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IPathConfigurationExpression<TSource, TDestination, TMember>> memberOptions)
        {
            if (TypeMap.IsOpenGeneric)
            {
                var rec = new RecordingPathConfig<TSource, TDestination, TMember>();
                memberOptions(rec);
                TypeMap.OpenGenericTypeMap.Steps.Add(new OpenPathStep { PathSegmentNames = GetMemberPathNames(destinationMember), Recipe = rec.Recipe });
                return this;
            }
            var members = GetMemberPathFromExpression(destinationMember);
            var path = new MemberPath(members);
            var lastMember = path.Last;
            var memberMap = new PathMap
            {
                DestinationName = string.Join(".", path.Members.Select(m => m.Name)),
                DestinationType = ReflectionHelper.GetMemberType(lastMember),
                MemberPath = path,
                DestinationExpression = destinationMember,
                SourceType = typeof(TSource),
                TypeMap = TypeMap,
                IsExplicitlyConfigured = true
            };
            ((List<PathMap>)TypeMap.PathMaps).Add(memberMap);
            var pathConfig = new PathConfigurationExpression<TSource, TDestination, TMember>(memberMap);
            memberOptions(pathConfig);
            return this;
        }

        private static string GetMemberNameFromExpression<TMember>(Expression<Func<TDestination, TMember>> expr)
            => GetMemberFromExpression(expr)?.Name;

        private static string[] GetMemberPathNames<TMember>(Expression<Func<TDestination, TMember>> expr)
            => GetMemberPathFromExpression(expr).Select(m => m.Name).ToArray();

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpression<TSource, TDestination>.ForSourceMember(Expression<Func<TSource, object>> sourceMember, Action<ISourceMemberConfigurationExpression> memberOptions)
        {
            var member = GetMemberFromExpression(sourceMember);
            if (member != null)
            {
                var cfg = TypeMap.FindOrCreateSourceMemberConfigFor(member);
                memberOptions(new SourceMemberConfigExpression(cfg));
            }
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> Include<TOtherSource, TOtherDestination>() where TOtherSource : TSource where TOtherDestination : TDestination
        {
            // A missing derived map is created at seal time, so declaring it explicitly is not a duplicate.
            TypeMap.IncludedDerivedTypes.Add(new TypePair(typeof(TOtherSource), typeof(TOtherDestination)));
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> IncludeBase<TSourceBase, TDestinationBase>() where TSourceBase : class where TDestinationBase : class
        {
            TypeMap.IncludedBaseTypes.Add(new TypePair(typeof(TSourceBase), typeof(TDestinationBase)));
            return this;
        }

        /// <inheritdoc />
        public void As<T>() where T : TDestination => TypeMap.DestinationTypeOverride = typeof(T);

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> IncludeMembers(params Expression<Func<TSource, object>>[] memberExpressions)
        {
            if (memberExpressions != null)
            {
                foreach (var expr in memberExpressions)
                {
                    var chain = MemberConfigurationExpression<TSource, object, object>.GetMemberPath(expr);
                    if (chain != null && chain.Length > 0)
                        TypeMap.IncludedMemberChains.Add(chain);
                }
            }
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> EqualityComparison(Expression<Func<TSource, TDestination, bool>> comparison)
        {
            if (comparison != null)
            {
                var func = comparison.Compile();
                TypeMap.EqualityComparison = (s, d) => func((TSource)s, (TDestination)d);
            }
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> ConvertUsingEnumMapping(Action<IEnumMappingExpression<TSource, TDestination>> options = null)
        {
            if (!typeof(TSource).IsEnum || !typeof(TDestination).IsEnum)
                throw new MapItConfigurationException($"ConvertUsingEnumMapping requires both {typeof(TSource).Name} and {typeof(TDestination).Name} to be enum types.")
                { Types = new TypePair(typeof(TSource), typeof(TDestination)) };

            var expr = new EnumMappingExpression<TSource, TDestination>();
            options?.Invoke(expr);
            if (expr.Overrides.Count > 0)
                TypeMap.EnumValueOverrides = expr.Overrides;
            else
                TypeMap.EnumValueOverrides = TypeMap.EnumValueOverrides ?? new System.Collections.Generic.Dictionary<object, object>();
            return this;
        }

        /// <inheritdoc />
        public void ForAllMembers(Action<IMemberConfigurationExpression<TSource, TDestination, object>> memberOptions)
        {
            foreach (var prop in TypeMap.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                var memberMap = TypeMap.FindOrCreatePropertyMapFor(prop, prop.PropertyType);
                var memberConfig = new MemberConfigurationExpression<TSource, TDestination, object>(TypeMap, Profile, memberMap, prop);
                memberOptions(memberConfig);
            }
        }

        #endregion

        #region IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, ResolutionContext, TDestination> ctor)
        {
            TypeMap.CustomCtor = new CustomCtor { Ctor = (s, ctx) => ctor((TSource)s, ctx) };
            return this;
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ConstructUsing(Expression<Func<TSource, TDestination>> ctor)
        {
            var func = ctor.Compile();
            TypeMap.CustomCtor = new CustomCtor { Ctor = (s, ctx) => func.DynamicInvoke(s) };
            return this;
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IProjectionExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ConstructUsing(Expression<Func<TSource, TDestination>> ctor)
        {
            var func = ctor.Compile();
            TypeMap.CustomCtor = new CustomCtor { Ctor = (s, ctx) => func.DynamicInvoke(s) };
            return this;
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IProjectionExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ForCtorParam(string ctorParamName, Action<ICtorParamConfigurationExpression<TSource>> paramOptions)
            => ForCtorParamCore(ctorParamName, paramOptions);

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ConstructUsingServiceLocator() => this;

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> DisableCtorValidation()
        {
            TypeMap.DisableConstructorValidation = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> beforeFunction)
        {
            TypeMap.AddBeforeMapAction(new MapAction { Action = (s, d, ctx) => beforeFunction((TSource)s, (TDestination)d) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination, ResolutionContext> beforeFunction)
        {
            TypeMap.AddBeforeMapAction(new MapAction { Action = (s, d, ctx) => beforeFunction((TSource)s, (TDestination)d, ctx) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> BeforeMap<TMappingAction>() where TMappingAction : IMappingAction<TSource, TDestination>
        {
            TypeMap.AddBeforeMapAction(new MapAction { Action = (s, d, ctx) => MapIt.Internal.ServiceFactory.Create<TMappingAction>(ctx).Process((TSource)s, (TDestination)d, ctx) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> afterFunction)
        {
            TypeMap.AddAfterMapAction(new MapAction { Action = (s, d, ctx) => afterFunction((TSource)s, (TDestination)d) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination, ResolutionContext> afterFunction)
        {
            TypeMap.AddAfterMapAction(new MapAction { Action = (s, d, ctx) => afterFunction((TSource)s, (TDestination)d, ctx) });
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> AfterMap<TMappingAction>() where TMappingAction : IMappingAction<TSource, TDestination>
        {
            TypeMap.AddAfterMapAction(new MapAction { Action = (s, d, ctx) => MapIt.Internal.ServiceFactory.Create<TMappingAction>(ctx).Process((TSource)s, (TDestination)d, ctx) });
            return this;
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.AsProxy() => this;

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> Include(Type derivedSourceType, Type derivedDestinationType)
        {
            TypeMap.IncludedDerivedTypes.Add(new TypePair(derivedSourceType, derivedDestinationType));
            return this;
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.IncludeAllDerived()
        {
            TypeMap.IncludeAllDerived = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> IncludeBase(Type sourceBase, Type destinationBase)
        {
            TypeMap.IncludedBaseTypes.Add(new TypePair(sourceBase, destinationBase));
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> MaxDepth(int depth)
        {
            TypeMap.MaxDepth = depth;
            TypeMap.HasExplicitMaxDepth = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> PreserveReferences()
        {
            TypeMap.PreserveReferences = true;
            return this;
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> ValidateMemberList(MemberList memberList)
        {
            TypeMap.ConfiguredMemberList = memberList;
            return this;
        }

        /// <inheritdoc />
        void IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.As(Type typeOverride)
        {
            if (typeOverride != null) TypeMap.DestinationTypeOverride = typeOverride;
        }

        /// <inheritdoc />
        public void ConvertUsing(ITypeConverter<TSource, TDestination> converter)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => converter.Convert((TSource)s, (TDestination)d, ctx) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Func<TSource, TDestination> mappingFunction)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => mappingFunction((TSource)s) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Func<TSource, TDestination, TDestination> mappingFunction)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => mappingFunction((TSource)s, (TDestination)d) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Func<TSource, TDestination, ResolutionContext, TDestination> mappingFunction)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => mappingFunction((TSource)s, (TDestination)d, ctx) };
        }

        /// <inheritdoc />
        public void ConvertUsing(Type typeConverterType)
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) =>
            {
                var converter = (ITypeConverter<TSource, TDestination>)MapIt.Internal.ServiceFactory.Create(typeConverterType, ctx);
                return converter.Convert((TSource)s, (TDestination)d, ctx);
            } };
        }

        /// <inheritdoc />
        public void ConvertUsing<TTypeConverter>() where TTypeConverter : ITypeConverter<TSource, TDestination>
        {
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => MapIt.Internal.ServiceFactory.Create<TTypeConverter>(ctx).Convert((TSource)s, (TDestination)d, ctx) };
        }

        /// <inheritdoc />
        void IProjectionExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ConvertUsing(Expression<Func<TSource, TDestination>> mappingExpression)
        {
            var func = mappingExpression.Compile();
            TypeMap.TypeConverter = new TypeConverter { Convert = (s, d, ctx) => (TDestination)func.DynamicInvoke(s) };
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ForCtorParam(string ctorParamName, Action<ICtorParamConfigurationExpression<TSource>> paramOptions)
            => ForCtorParamCore(ctorParamName, paramOptions);

        private IMappingExpression<TSource, TDestination> ForCtorParamCore(string ctorParamName, Action<ICtorParamConfigurationExpression<TSource>> paramOptions)
        {
            if (TypeMap.IsOpenGeneric)
            {
                var rec = new RecordingCtorParamConfig<TSource>();
                paramOptions(rec);
                TypeMap.OpenGenericTypeMap.Steps.Add(new OpenCtorParamStep { CtorParamName = ctorParamName, Recipe = rec.Recipe });
                return this;
            }
            var cfg = new CtorParamConfig<TSource>(TypeMap, ctorParamName);
            paramOptions(cfg);
            return this;
        }

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>.ForSourceMember(string sourceMemberName, Action<ISourceMemberConfigurationExpression> memberOptions)
        {
            var member = ReflectionHelper.GetFieldOrProperty(TypeMap.SourceType, sourceMemberName);
            if (member != null)
            {
                var cfg = TypeMap.FindOrCreateSourceMemberConfigFor(member);
                memberOptions(new SourceMemberConfigExpression(cfg));
            }
            return this;
        }

        #endregion

        #region IProjectionExpression<TSource, TDestination, IMappingExpression<TSource, TDestination>>

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IProjectionExpression<TSource, TDestination, IMappingExpression<TSource, TDestination>>.AddTransform<TValue>(Expression<Func<TValue, TValue>> transformer) => this;

        /// <inheritdoc />
        IMappingExpression<TSource, TDestination> IProjectionExpression<TSource, TDestination, IMappingExpression<TSource, TDestination>>.IncludeMembers(Expression<Func<TSource, object>>[] memberExpressions)
        {
            if (memberExpressions != null)
            {
                foreach (var expr in memberExpressions)
                {
                    var chain = MemberConfigurationExpression<TSource, object, object>.GetMemberPath(expr);
                    if (chain != null && chain.Length > 0)
                        TypeMap.IncludedMemberChains.Add(chain);
                }
            }
            return this;
        }

        #endregion

        #region Helpers

        internal static MemberInfo GetMemberFromExpression<TDest, TMember>(Expression<Func<TDest, TMember>> expr)
        {
            if (expr.Body is MemberExpression me) return me.Member;
            if (expr.Body is UnaryExpression ue && ue.Operand is MemberExpression me2) return me2.Member;
            return null;
        }

        internal static MemberInfo[] GetMemberPathFromExpression<TDest, TMember>(Expression<Func<TDest, TMember>> expr)
        {
            var members = new List<MemberInfo>();
            var node = expr.Body;
            while (node is MemberExpression me)
            {
                members.Insert(0, me.Member);
                node = me.Expression;
            }
            return members.ToArray();
        }

        #endregion
    }
}
