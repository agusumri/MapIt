using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Translates an expression written against a destination/DTO type into the equivalent
    /// expression against the source/entity type, by rewriting each destination member access to
    /// the source member(s) its map resolves from (MapIt's UseAsDataSource / expression mapping).
    /// Only expression-shaped, member-based maps are translatable; other constructs throw.
    /// </summary>
    internal static class ExpressionTranslator
    {
        /// <summary>Translates a predicate (or selector) on TDestination into one on TSource.</summary>
        public static LambdaExpression Translate(MapperConfiguration configuration, Type destinationType, Type sourceType, LambdaExpression destExpression)
        {
            var typeMap = configuration.FindTypeMapFor(sourceType, destinationType);
            if (typeMap == null)
                throw TypeMap.MissingMapException(sourceType, destinationType);

            var srcParam = Expression.Parameter(sourceType, destExpression.Parameters[0].Name ?? "src");
            var visitor = new TranslationVisitor(configuration, typeMap, destExpression.Parameters[0], srcParam, sourceType, destinationType);
            var body = visitor.Visit(destExpression.Body);
            return Expression.Lambda(body, srcParam);
        }

        private sealed class TranslationVisitor : ExpressionVisitor
        {
            private readonly MapperConfiguration _configuration;
            private readonly TypeMap _typeMap;
            private readonly ParameterExpression _destParam;
            private readonly ParameterExpression _srcParam;
            private readonly Type _sourceType;
            private readonly Type _destinationType;

            public TranslationVisitor(MapperConfiguration configuration, TypeMap typeMap, ParameterExpression destParam,
                ParameterExpression srcParam, Type sourceType, Type destinationType)
            {
                _configuration = configuration;
                _typeMap = typeMap;
                _destParam = destParam;
                _srcParam = srcParam;
                _sourceType = sourceType;
                _destinationType = destinationType;
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _destParam ? _srcParam : base.VisitParameter(node);

            protected override Expression VisitMember(MemberExpression node)
            {
                // Rewrite only accesses rooted at the destination parameter.
                if (RootsAtDestParam(node))
                {
                    var sourceAccess = BuildSourceAccess(node);
                    if (sourceAccess != null) return sourceAccess;
                    throw new MapItMappingException(
                        $"Cannot translate member '{node.Member.Name}' of {_destinationType.Name} to {_sourceType.Name}: it is not mapped from a translatable source member chain.")
                    { Types = new TypePair(_sourceType, _destinationType) };
                }
                return base.VisitMember(node);
            }

            private bool RootsAtDestParam(Expression node)
            {
                while (node is MemberExpression me) node = me.Expression;
                return node == _destParam;
            }

            /// <summary>
            /// For a destination member access chain (d.A.B...), produce the source access chain
            /// (src.X.Y...) using each level's type map member resolution.
            /// </summary>
            private Expression BuildSourceAccess(MemberExpression node)
            {
                // Flatten the destination access chain into member names (outermost last).
                var names = new List<string>();
                Expression cur = node;
                while (cur is MemberExpression me)
                {
                    names.Insert(0, me.Member.Name);
                    cur = me.Expression;
                }
                if (cur != _destParam) return null;

                Expression access = _srcParam;
                var currentSourceType = _sourceType;
                var currentDestType = _destinationType;

                foreach (var destName in names)
                {
                    var map = _configuration.FindTypeMapFor(currentSourceType, currentDestType);
                    if (map == null) return null;
                    var pm = map.PropertyMaps.FirstOrDefault(p => string.Equals(p.DestinationName, destName, StringComparison.Ordinal) && !p.Ignored);
                    // Only member-chain maps are translatable (no resolver/converter/custom delegate).
                    if (pm == null || pm.Resolver != null || pm.SourceMembers == null || pm.SourceMembers.Length == 0)
                        return null;

                    foreach (var sm in pm.SourceMembers)
                        access = MakeAccess(access, sm);

                    currentSourceType = ReflectionHelper.GetMemberType(pm.SourceMembers[pm.SourceMembers.Length - 1]);
                    currentDestType = pm.DestinationType;
                }
                return access;
            }

            private static Expression MakeAccess(Expression target, MemberInfo member)
            {
                if (member is PropertyInfo pi) return Expression.Property(target, pi);
                if (member is FieldInfo fi) return Expression.Field(target, fi);
                throw new MapItMappingException($"Member {member.Name} cannot be translated.");
            }
        }
    }
}
