using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>Closed ctor-parameter configuration that mutates the type map's ConstructorParameterMaps.</summary>
    internal sealed class CtorParamConfig : ICtorParamConfigurationExpression<object>, ICtorParamConfigurationExpression
    {
        private readonly TypeMap _typeMap;
        private readonly ConstructorParameterMap _map;

        public CtorParamConfig(TypeMap typeMap, string ctorParamName)
        {
            _typeMap = typeMap;
            _map = typeMap.ConstructorParameterMaps.FirstOrDefault(m => string.Equals(m.DestinationName, ctorParamName, StringComparison.OrdinalIgnoreCase));
            if (_map == null)
            {
                _map = new ConstructorParameterMap { DestinationName = ctorParamName, SourceType = typeMap.SourceType, TypeMap = typeMap, IsExplicitlyConfigured = true };
                typeMap.ConstructorParameterMaps.Add(_map);
            }
        }

        public void MapFrom(string sourceMembersPath)
        {
            var chain = new List<MemberInfo>();
            var type = _typeMap.SourceType;
            foreach (var name in sourceMembersPath.Split('.'))
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, name);
                if (member == null) throw new ArgumentException($"Source {type.Name} has no member '{name}'.");
                chain.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            _map.SourceMembers = chain.ToArray();
        }

        public void ExplicitExpansion() { }

        public void MapFrom<TMember>(Expression<Func<object, TMember>> sourceMember)
        {
            var chain = MemberConfigurationExpression<object, object, object>.GetMemberPath(sourceMember);
            if (chain != null && chain.Length > 0 && IsSimple(sourceMember))
                _map.SourceMembers = chain;
            else
            {
                var func = sourceMember.Compile();
                _map.Resolver = new ValueResolverMember((s, d, dm, ctx) => func(s));
            }
        }

        public void MapFrom<TMember>(Func<object, ResolutionContext, TMember> resolver)
            => _map.Resolver = new ValueResolverMember((s, d, dm, ctx) => resolver(s, ctx));

        private static bool IsSimple<TMember>(Expression<Func<object, TMember>> expr)
        {
            var body = expr.Body;
            if (body is UnaryExpression ue) body = ue.Operand;
            return body is MemberExpression;
        }
    }

    /// <summary>Typed closed ctor-parameter configuration.</summary>
    internal sealed class CtorParamConfig<TSource> : ICtorParamConfigurationExpression<TSource>, ICtorParamConfigurationExpression
    {
        private readonly TypeMap _typeMap;
        private readonly ConstructorParameterMap _map;

        public CtorParamConfig(TypeMap typeMap, string ctorParamName)
        {
            _typeMap = typeMap;
            _map = typeMap.ConstructorParameterMaps.FirstOrDefault(m => string.Equals(m.DestinationName, ctorParamName, StringComparison.OrdinalIgnoreCase));
            if (_map == null)
            {
                _map = new ConstructorParameterMap { DestinationName = ctorParamName, SourceType = typeMap.SourceType, TypeMap = typeMap, IsExplicitlyConfigured = true };
                typeMap.ConstructorParameterMaps.Add(_map);
            }
        }

        public void MapFrom(string sourceMembersPath)
        {
            var chain = new List<MemberInfo>();
            var type = _typeMap.SourceType;
            foreach (var name in sourceMembersPath.Split('.'))
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, name);
                if (member == null) throw new ArgumentException($"Source {type.Name} has no member '{name}'.");
                chain.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            _map.SourceMembers = chain.ToArray();
        }

        public void ExplicitExpansion() { }

        public void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember)
        {
            var chain = MemberConfigurationExpression<TSource, object, object>.GetMemberPath(sourceMember);
            var body = sourceMember.Body;
            if (body is UnaryExpression ue) body = ue.Operand;
            if (chain != null && chain.Length > 0 && body is MemberExpression)
                _map.SourceMembers = chain;
            else
            {
                var func = sourceMember.Compile();
                _map.Resolver = new ValueResolverMember((s, d, dm, ctx) => func((TSource)s));
            }
        }

        public void MapFrom<TMember>(Func<TSource, ResolutionContext, TMember> resolver)
            => _map.Resolver = new ValueResolverMember((s, d, dm, ctx) => resolver((TSource)s, ctx));
    }

    /// <summary>Records an open-generic ctor-param configuration as a recipe.</summary>
    internal sealed class RecordingCtorParamConfig : ICtorParamConfigurationExpression<object>, ICtorParamConfigurationExpression
    {
        public OpenValueRecipe Recipe { get; } = new OpenValueRecipe();

        public void MapFrom(string sourceMembersPath) => Recipe.SourceMemberNames = sourceMembersPath?.Split('.');
        public void ExplicitExpansion() { }
        public void MapFrom<TMember>(Expression<Func<object, TMember>> sourceMember) => Recipe.RawExpression = sourceMember;
        public void MapFrom<TMember>(Func<object, ResolutionContext, TMember> resolver)
            => throw new MapItMappingException("Open-generic ForCtorParam with a runtime delegate cannot be rebound; use MapFrom(name) or an expression.");
    }

    /// <summary>Typed recording open-generic ctor-param configuration.</summary>
    internal sealed class RecordingCtorParamConfig<TSource> : ICtorParamConfigurationExpression<TSource>, ICtorParamConfigurationExpression
    {
        public OpenValueRecipe Recipe { get; } = new OpenValueRecipe();

        public void MapFrom(string sourceMembersPath) => Recipe.SourceMemberNames = sourceMembersPath?.Split('.');
        public void ExplicitExpansion() { }
        public void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember) => Recipe.RawExpression = sourceMember;
        public void MapFrom<TMember>(Func<TSource, ResolutionContext, TMember> resolver)
            => throw new MapItMappingException("Open-generic ForCtorParam with a runtime delegate cannot be rebound; use MapFrom(name) or an expression.");
    }

    /// <summary>Source-member configuration (DoNotValidate).</summary>
    internal sealed class SourceMemberConfigExpression : ISourceMemberConfigurationExpression
    {
        private readonly SourceMemberConfig _config;
        public SourceMemberConfigExpression(SourceMemberConfig config) => _config = config;
        public void DoNotValidate() => _config.Ignore();
    }
}
