using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Member configuration expression with untyped source/destination.
    /// </summary>
    public class MemberConfigurationExpression :
        IMemberConfigurationExpression,
        IProjectionMemberConfiguration<object, object, object>
    {
        internal MemberConfigurationExpression(TypeMap typeMap, Profile profile, PropertyMap memberMap, MemberInfo destMember)
        {
            TypeMap = typeMap;
            Profile = profile;
            MemberMap = memberMap;
            DestinationMember = destMember;
            if (memberMap != null) memberMap.IsExplicitlyConfigured = true;
        }

        internal TypeMap TypeMap { get; }
        internal Profile Profile { get; }
        internal PropertyMap MemberMap { get; }

        /// <inheritdoc />
        public MemberInfo DestinationMember { get; }

        /// <inheritdoc />
        public void MapFrom(Type valueResolverType)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
            {
                var resolver = (IValueResolver<object, object, object>)Activator.CreateInstance(valueResolverType);
                return resolver.Resolve(s, d, dm, ctx);
            });
        }

        /// <inheritdoc />
        public void MapFrom(Type valueResolverType, string sourceMemberName)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
            {
                var resolver = (IValueResolver<object, object, object>)Activator.CreateInstance(valueResolverType);
                return resolver.Resolve(s, d, dm, ctx);
            });
        }

        /// <inheritdoc />
        public void MapFrom<TSource, TDestination, TSourceMember, TDestMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TDestMember> resolver, string sourceMemberName)
        {
            MemberMap.Resolver = new MemberValueResolverMember(
                (s, d, sm, dm, ctx) => resolver.Resolve((TSource)s, (TDestination)d, (TSourceMember)sm, (TDestMember)dm, ctx),
                null);
        }

        /// <inheritdoc />
        public void ConvertUsing(Type valueConverterType)
        {
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
            {
                var converter = (IValueConverter<object, object>)Activator.CreateInstance(valueConverterType);
                return converter.Convert(sm, ctx);
            });
        }

        /// <inheritdoc />
        public void ConvertUsing(Type valueConverterType, string sourceMemberName)
        {
            ConvertUsing(valueConverterType);
        }

        /// <inheritdoc />
        public void ConvertUsing<TSourceMember, TDestinationMember>(IValueConverter<TSourceMember, TDestinationMember> valueConverter, string sourceMemberName)
        {
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                valueConverter.Convert((TSourceMember)sm, ctx));
        }

        #region IProjectionMemberConfiguration

        /// <inheritdoc />
        public void AddTransform(Expression<Func<object, object>> transformer) { }
        /// <inheritdoc />
        public void AllowNull() { }
        /// <inheritdoc />
        public void DoNotAllowNull() { }
        /// <inheritdoc />
        public void ExplicitExpansion() { }

        /// <inheritdoc />
        public void Ignore() => MemberMap.Ignored = true;

        /// <inheritdoc />
        public void MapFrom<TSourceMember>(Expression<Func<object, TSourceMember>> mapExpression)
        {
            MemberInfo member = null;
            if (mapExpression.Body is MemberExpression me) member = me.Member;
            else if (mapExpression.Body is UnaryExpression ue && ue.Operand is MemberExpression me2) member = me2.Member;
            if (member != null) MemberMap.SourceMembers = new[] { member };
        }

        /// <inheritdoc />
        public void NullSubstitute(object nullSubstitute) => MemberMap.NullSubstitute = nullSubstitute;

        #endregion
    }

    /// <summary>
    /// Member configuration expression with typed source/destination/member.
    /// </summary>
    public class MemberConfigurationExpression<TSource, TDestination, TMember> :
        IMemberConfigurationExpression<TSource, TDestination, TMember>,
        IMemberConfigurationExpression,
        IProjectionMemberConfiguration<TSource, TDestination, TMember>
    {
        internal MemberConfigurationExpression(TypeMap typeMap, Profile profile, PropertyMap memberMap, MemberInfo destMember)
        {
            TypeMap = typeMap;
            Profile = profile;
            MemberMap = memberMap;
            DestinationMember = destMember;
            if (memberMap != null) memberMap.IsExplicitlyConfigured = true;
        }

        internal TypeMap TypeMap { get; }
        internal Profile Profile { get; }
        internal PropertyMap MemberMap { get; }

        /// <inheritdoc />
        public MemberInfo DestinationMember { get; }

        #region IMemberConfigurationExpression<TSource, TDestination, TMember>

        /// <inheritdoc />
        public void MapFrom(string sourceMembersPath)
        {
            var memberNames = sourceMembersPath.Split('.');
            var chain = new List<MemberInfo>();
            var type = TypeMap.SourceType;
            foreach (var name in memberNames)
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, name);
                if (member == null)
                    throw new ArgumentException($"Source {type.Name} has no member '{name}'.");
                chain.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            MemberMap.SourceMembers = chain.ToArray();
        }

        /// <inheritdoc />
        public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> mapExpression)
        {
            // If it's a simple member chain, use SourceMembers; otherwise compile as a custom expression
            var chain = GetMemberPath(mapExpression);
            if (chain != null && chain.Length > 0 && IsSimpleMemberPath(mapExpression))
            {
                MemberMap.SourceMembers = chain;
            }
            else
            {
                var func = mapExpression.Compile();
                MemberMap.CustomMapExpression = mapExpression;
                MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                    func.DynamicInvoke((TSource)s));
            }
        }

        /// <inheritdoc />
        public void MapFrom<TResult>(Func<TSource, TDestination, TResult> mappingFunction)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                (object)mappingFunction((TSource)s, (TDestination)d));
        }

        /// <inheritdoc />
        public void MapFrom<TResult>(Func<TSource, TDestination, TMember, TResult> mappingFunction)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                (object)mappingFunction((TSource)s, (TDestination)d, (TMember)dm));
        }

        /// <inheritdoc />
        public void MapFrom<TResult>(Func<TSource, TDestination, TMember, ResolutionContext, TResult> mappingFunction)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                (object)mappingFunction((TSource)s, (TDestination)d, (TMember)dm, ctx));
        }

        /// <inheritdoc />
        public void MapFrom(IValueResolver<TSource, TDestination, TMember> valueResolver)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                (object)valueResolver.Resolve((TSource)s, (TDestination)d, (TMember)dm, ctx));
        }

        /// <inheritdoc />
        public void MapFrom<TSourceMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TMember> valueResolver, Expression<Func<TSource, TSourceMember>> sourceMember)
        {
            var chain = GetMemberPath(sourceMember);
            var accessor = chain != null && chain.Length > 0
                ? (Func<object, object>)(s => GetChainValue(s, chain))
                : null;
            MemberMap.Resolver = new MemberValueResolverMember(
                (s, d, sm, dm, ctx) => (object)valueResolver.Resolve((TSource)s, (TDestination)d, (TSourceMember)sm, (TMember)dm, ctx),
                accessor);
        }

        /// <inheritdoc />
        public void MapFrom<TValueResolver, TSourceMember>(string sourceMemberName)
        {
            var resolver = (IMemberValueResolver<TSource, TDestination, TSourceMember, TMember>)Activator.CreateInstance(typeof(TValueResolver));
            // Resolve the source member by name from the source type
            var chain = ResolveChainByName(sourceMemberName);
            var accessor = chain != null && chain.Length > 0
                ? (Func<object, object>)(s => GetChainValue(s, chain))
                : null;
            MemberMap.Resolver = new MemberValueResolverMember(
                (s, d, sm, dm, ctx) => (object)resolver.Resolve((TSource)s, (TDestination)d, (TSourceMember)sm, (TMember)dm, ctx),
                accessor);
        }

        /// <inheritdoc />
        public void MapFrom<TValueResolver, TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember)
        {
            var resolver = (IMemberValueResolver<TSource, TDestination, TSourceMember, TMember>)Activator.CreateInstance(typeof(TValueResolver));
            MapFrom(resolver, sourceMember);
        }

        /// <inheritdoc />
        public void MapFrom<TValueResolver>() where TValueResolver : IValueResolver<TSource, TDestination, TMember>
        {
            var resolverType = typeof(TValueResolver);
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
            {
                // Created per call via the service constructor (DI-friendly), like other resolver types.
                var resolver = (TValueResolver)(ctx?.ServiceCtor != null ? ctx.ServiceCtor(resolverType) : Activator.CreateInstance(resolverType));
                return resolver.Resolve((TSource)s, (TDestination)d, dm is TMember m ? m : default, ctx);
            });
        }

        /// <inheritdoc />
        public void Condition(Func<TSource, bool> condition) =>
            MemberMap.ConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s);

        /// <inheritdoc />
        public void Condition(Func<TSource, TDestination, bool> condition) =>
            MemberMap.ConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s, (TDestination)d);

        /// <inheritdoc />
        public void Condition(Func<TSource, TDestination, TMember, bool> condition) =>
            MemberMap.ConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s, (TDestination)d, (TMember)sm);

        /// <inheritdoc />
        public void Condition(Func<TSource, TDestination, TMember, TMember, bool> condition) =>
            MemberMap.ConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s, (TDestination)d, (TMember)sm, (TMember)dm);

        /// <inheritdoc />
        public void Condition(Func<TSource, TDestination, TMember, TMember, ResolutionContext, bool> condition) =>
            MemberMap.ConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s, (TDestination)d, (TMember)sm, (TMember)dm, ctx);

        /// <inheritdoc />
        public void PreCondition(Func<ResolutionContext, bool> condition) =>
            MemberMap.PreConditionDelegate = (s, d, sm, dm, ctx) => condition(ctx);

        /// <inheritdoc />
        public void PreCondition(Func<TSource, bool> condition) =>
            MemberMap.PreConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s);

        /// <inheritdoc />
        public void PreCondition(Func<TSource, ResolutionContext, bool> condition) =>
            MemberMap.PreConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s, ctx);

        /// <inheritdoc />
        public void PreCondition(Func<TSource, TDestination, ResolutionContext, bool> condition) =>
            MemberMap.PreConditionDelegate = (s, d, sm, dm, ctx) => condition((TSource)s, (TDestination)d, ctx);

        /// <inheritdoc />
        public void UseDestinationValue() => MemberMap.UseDestinationValue = true;

        /// <inheritdoc />
        public void DoNotUseDestinationValue() => MemberMap.UseDestinationValue = false;

        /// <inheritdoc />
        public void MapAtRuntime() => MemberMap.MapAtRuntime = true;

        /// <inheritdoc />
        public void SetMappingOrder(int mappingOrder) => MemberMap.MappingOrder = mappingOrder;

        /// <inheritdoc />
        public void MapFromAsync(IAsyncValueResolver<TSource, TDestination, TMember> asyncResolver)
        {
            if (asyncResolver == null) return;
            MemberMap.AsyncResolver = async (s, d, dm, ctx, ct) =>
                (object)await asyncResolver.ResolveAsync((TSource)s, (TDestination)d, (TMember)(dm ?? default(TMember)), ctx, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void MapFromAsync(System.Func<TSource, System.Threading.Tasks.Task<TMember>> asyncResolver)
        {
            if (asyncResolver == null) return;
            MemberMap.AsyncResolver = async (s, d, dm, ctx, ct) =>
                (object)await asyncResolver((TSource)s).ConfigureAwait(false);
        }

        #endregion

        #region IMemberConfigurationExpression (typed)

        /// <inheritdoc />
        public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> valueConverter, Expression<Func<TSource, TSourceMember>> sourceMember)
        {
            UseConverterSource(sourceMember);
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                (object)valueConverter.Convert((TSourceMember)sm, ctx));
        }

        /// <summary>
        /// Points the converter at the given source member. The engine resolves
        /// <see cref="MemberMap.SourceMembers"/> and passes that value to the converter.
        /// </summary>
        private void UseConverterSource<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember)
        {
            var chain = sourceMember == null ? null : GetMemberPath(sourceMember);
            if (chain != null && chain.Length > 0)
                MemberMap.SourceMembers = chain;
        }

        /// <inheritdoc />
        public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> valueConverter, string sourceMemberName)
        {
            var chain = ResolveChainByName(sourceMemberName);
            if (chain != null && chain.Length > 0)
                MemberMap.SourceMembers = chain;
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                (object)valueConverter.Convert((TSourceMember)sm, ctx));
        }

        /// <inheritdoc />
        public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> valueConverter)
        {
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                (object)valueConverter.Convert((TSourceMember)sm, ctx));
        }

        /// <inheritdoc />
        public void ConvertUsing<TValueConverter, TSourceMember>() where TValueConverter : IValueConverter<TSourceMember, TMember>
        {
            // Created per call via the service constructor (DI-friendly).
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                (object)MapIt.Internal.ServiceFactory.Create<TValueConverter>(ctx).Convert((TSourceMember)sm, ctx));
        }

        /// <inheritdoc />
        public void ConvertUsing<TValueConverter, TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember) where TValueConverter : IValueConverter<TSourceMember, TMember>
        {
            UseConverterSource(sourceMember);
            ConvertUsing<TValueConverter, TSourceMember>();
        }

        /// <inheritdoc />
        public void ConvertUsing<TValueConverter, TSourceMember>(string sourceMemberName) where TValueConverter : IValueConverter<TSourceMember, TMember>
        {
            var chain = ResolveChainByName(sourceMemberName);
            if (chain != null && chain.Length > 0)
                MemberMap.SourceMembers = chain;
            ConvertUsing<TValueConverter, TSourceMember>();
        }

        #endregion

        #region IMemberConfigurationExpression (base)

        void IMemberConfigurationExpression.MapFrom(Type valueResolverType)
        {
            MemberMap.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
            {
                var resolver = (IValueResolver<TSource, TDestination, TMember>)Activator.CreateInstance(valueResolverType);
                return resolver.Resolve((TSource)s, (TDestination)d, (TMember)dm, ctx);
            });
        }

        void IMemberConfigurationExpression.MapFrom(Type valueResolverType, string sourceMemberName)
        {
            ((IMemberConfigurationExpression)this).MapFrom(valueResolverType);
        }

        void IMemberConfigurationExpression.MapFrom<TSource, TDestination, TSourceMember, TDestMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TDestMember> resolver, string sourceMemberName)
        {
            MemberMap.Resolver = new MemberValueResolverMember(
                (s, d, sm, dm, ctx) => resolver.Resolve((TSource)s, (TDestination)d, (TSourceMember)sm, (TDestMember)dm, ctx),
                null);
        }

        void IMemberConfigurationExpression.ConvertUsing(Type valueConverterType)
        {
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
            {
                var converter = (IValueConverter<object, object>)Activator.CreateInstance(valueConverterType);
                return converter.Convert(sm, ctx);
            });
        }

        void IMemberConfigurationExpression.ConvertUsing(Type valueConverterType, string sourceMemberName)
        {
            ((IMemberConfigurationExpression)this).ConvertUsing(valueConverterType);
        }

        void IMemberConfigurationExpression.ConvertUsing<TSourceMember, TDestinationMember>(IValueConverter<TSourceMember, TDestinationMember> valueConverter, string sourceMemberName)
        {
            var chain = ResolveChainByName(sourceMemberName);
            if (chain != null && chain.Length > 0)
                MemberMap.SourceMembers = chain;
            MemberMap.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                (object)valueConverter.Convert((TSourceMember)sm, ctx));
        }

        #endregion

        #region IProjectionMemberConfiguration

        /// <inheritdoc />
        public void AddTransform(Expression<Func<TMember, TMember>> transformer)
        {
            if (transformer == null) return;
            var func = transformer.Compile();
            MemberMap.AddValueTransformation(new ValueTransformerConfiguration(typeof(TMember), v => func((TMember)v)));
        }
        /// <inheritdoc />
        public void AllowNull() => MemberMap.AllowNull = true;
        /// <inheritdoc />
        public void DoNotAllowNull() => MemberMap.AllowNull = false;
        /// <inheritdoc />
        public void ExplicitExpansion() => MemberMap.IsExplicitExpansion = true;

        /// <inheritdoc />
        public void Ignore() => MemberMap.Ignored = true;

        /// <inheritdoc />
        void IProjectionMemberConfiguration<TSource, TDestination, TMember>.MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> mapExpression)
        {
            MemberMap.SourceMembers = GetMemberPath(mapExpression);
        }

        /// <inheritdoc />
        public void NullSubstitute(object nullSubstitute) => MemberMap.NullSubstitute = nullSubstitute;

        #endregion

        #region Helpers

        /// <summary>Resolves a source member chain by dotted name from the source type.</summary>
        private MemberInfo[] ResolveChainByName(string sourceMembersPath)
        {
            var memberNames = sourceMembersPath.Split('.');
            var chain = new List<MemberInfo>();
            var type = TypeMap.SourceType;
            foreach (var name in memberNames)
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, name);
                if (member == null) return null;
                chain.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            return chain.ToArray();
        }

        /// <summary>Whether the expression is a simple member access path (no computed operations).</summary>
        private static bool IsSimpleMemberPath<TDest, TMember>(Expression<Func<TDest, TMember>> expr)
        {
            var body = expr.Body;
            if (body is UnaryExpression ue) body = ue.Operand; // handle Convert/Quote
            if (body is MemberExpression) return true;
            return false;
        }

        internal static MemberInfo[] GetMemberPath<TSourceType, TMember>(Expression<Func<TSourceType, TMember>> expr)
        {
            if (expr.Body is MemberExpression me)
                return new[] { me.Member };
            if (expr.Body is UnaryExpression ue && ue.Operand is MemberExpression me2)
                return new[] { me2.Member };
            // Nested path
            var members = new List<MemberInfo>();
            var node = expr.Body;
            while (node is MemberExpression m)
            {
                members.Insert(0, m.Member);
                node = m.Expression;
            }
            return members.ToArray();
        }

        internal static object GetChainValue(object source, MemberInfo[] chain)
        {
            object current = source;
            foreach (var member in chain)
            {
                if (current == null) return null;
                current = ReflectionHelper.GetMemberValue(member, current);
            }
            return current;
        }

        #endregion
    }

    /// <summary>
    /// Path configuration expression.
    /// </summary>
    public class PathConfigurationExpression<TSource, TDestination, TMember> :
        IPathConfigurationExpression<TSource, TDestination, TMember>
    {
        internal PathConfigurationExpression(PathMap pathMap)
        {
            PathMap = pathMap;
        }

        internal PathMap PathMap { get; }

        /// <inheritdoc />
        public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember)
        {
            var chain = MemberConfigurationExpression<TSource, TDestination, TMember>.GetMemberPath(sourceMember);
            PathMap.SourceMembers = chain;
        }

        /// <inheritdoc />
        public void Ignore() => PathMap.Ignored = true;

        /// <inheritdoc />
        public void Condition(Func<ConditionParameters<TSource, TDestination, TMember>, bool> condition)
        {
            PathMap.ConditionDelegate = (s, d, sm, dm, ctx) =>
                condition(new ConditionParameters<TSource, TDestination, TMember>((TSource)s, (TDestination)d, (TMember)sm, (TMember)dm, ctx));
        }
    }
}
