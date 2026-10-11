using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Builds a provider-translatable projection expression (a member-init Select lambda) from a
    /// sealed type map (BEHAVIOR §12 / Gap 4). Untranslatable constructs throw a clear
    /// <see cref="MapItMappingException"/> at build time rather than evaluating in memory.
    /// </summary>
    internal static class ProjectionBuilder
    {
        public static LambdaExpression Build(MapperConfiguration configuration, Type sourceType, Type destinationType,
            IDictionary<string, object> parameters, HashSet<string> membersToExpand)
        {
            var param = Expression.Parameter(sourceType, "src");
            var body = BuildMemberInit(configuration, sourceType, destinationType, param, parameters, membersToExpand, 0);
            return Expression.Lambda(body, param);
        }

        private static Expression BuildMemberInit(MapperConfiguration configuration, Type sourceType, Type destinationType,
            Expression sourceAccess, IDictionary<string, object> parameters, HashSet<string> membersToExpand, int depth)
        {
            var typeMap = configuration.FindTypeMapFor(sourceType, destinationType);
            if (typeMap == null)
                throw TypeMap.MissingMapException(sourceType, destinationType) as MapItMappingException
                      ?? new MapItMappingException($"Missing map {sourceType.Name} -> {destinationType.Name}.");

            RejectUntranslatableMapLevel(typeMap, sourceType, destinationType);

            var ctor = destinationType.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                throw new MapItMappingException(
                    $"Cannot project to {destinationType.Name}: it has no parameterless constructor required for a member-init projection.")
                { Types = new TypePair(sourceType, destinationType) };

            var bindings = new List<MemberBinding>();
            foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                var propMap = typeMap.PropertyMaps.FirstOrDefault(p => string.Equals(p.DestinationName, destProp.Name, StringComparison.Ordinal));
                if (propMap != null && propMap.Ignored) continue;

                if (propMap != null && propMap.Resolver != null)
                    throw Untranslatable(destProp.Name, sourceType, destinationType, "a resolver/converter or runtime delegate");

                // Explicit-expansion gating.
                if (propMap != null && propMap.IsExplicitExpansion &&
                    (membersToExpand == null || !membersToExpand.Contains(destProp.Name)))
                    continue;

                var sourceChain = ResolveSourceChain(typeMap, propMap, destProp, sourceType);
                if (sourceChain == null) continue;

                var valueExpr = BuildValueExpression(configuration, sourceAccess, sourceChain, destProp.PropertyType,
                    parameters, membersToExpand, depth);
                if (valueExpr == null) continue;

                bindings.Add(Expression.Bind(destProp, valueExpr));
            }

            return Expression.MemberInit(Expression.New(ctor), bindings);
        }

        private static void RejectUntranslatableMapLevel(TypeMap typeMap, Type sourceType, Type destinationType)
        {
            if (typeMap.TypeConverter != null)
                throw Untranslatable(null, sourceType, destinationType, "a ConvertUsing type converter");
            if (typeMap.CustomCtor != null)
                throw Untranslatable(null, sourceType, destinationType, "a ConstructUsing delegate");
            if (typeMap.BeforeMapActions.Count > 0 || typeMap.AfterMapActions.Count > 0)
                throw Untranslatable(null, sourceType, destinationType, "a Before/After map action");
            if (typeMap.ConstructorParameterMaps.Count > 0)
                throw Untranslatable(null, sourceType, destinationType, "a ForCtorParam configuration");
        }

        private static MemberInfo[] ResolveSourceChain(TypeMap typeMap, PropertyMap propMap, PropertyInfo destProp, Type sourceType)
        {
            if (propMap != null && propMap.SourceMembers != null && propMap.SourceMembers.Length > 0)
                return propMap.SourceMembers;

            // Convention: a source member of the same name.
            var member = ReflectionHelper.GetFieldOrProperty(sourceType, destProp.Name);
            return member != null ? new[] { member } : null;
        }

        private static Expression BuildValueExpression(MapperConfiguration configuration, Expression sourceAccess,
            MemberInfo[] chain, Type destMemberType, IDictionary<string, object> parameters, HashSet<string> membersToExpand, int depth)
        {
            // Walk the chain with EF-equivalent null propagation.
            Expression current = sourceAccess;
            Expression nullGuard = null;
            foreach (var member in chain)
            {
                current = MakeMemberAccess(current, member);
                var declType = current.Type;
                if (!declType.IsValueType || Nullable.GetUnderlyingType(declType) != null)
                {
                    var test = Expression.NotEqual(current, Expression.Constant(null, current.Type));
                    nullGuard = nullGuard == null ? (Expression)test : Expression.AndAlso(nullGuard, test);
                }
            }

            var sourceMemberType = current.Type;
            Expression value = current;

            // Collection projection: source collection -> destination collection of a mapped element type.
            var srcElement = ReflectionHelper.GetEnumerableElementType(sourceMemberType);
            var destElement = ReflectionHelper.GetEnumerableElementType(destMemberType);
            if (srcElement != null && destElement != null && !IsSimple(srcElement) && srcElement != destElement
                && configuration.FindTypeMapFor(srcElement, destElement) != null)
            {
                if (depth >= configuration.RecursiveQueriesMaxDepth)
                    return Expression.Constant(GetDefault(destMemberType), destMemberType);

                var collectionExpr = BuildCollectionProjection(configuration, current, srcElement, destElement, destMemberType, parameters, membersToExpand, depth);
                if (collectionExpr != null)
                {
                    if (nullGuard != null)
                        return Expression.Condition(nullGuard, collectionExpr, Expression.Constant(GetDefault(destMemberType), destMemberType));
                    return collectionExpr;
                }
            }

            // Nested type map -> recursive member-init (truncate at RecursiveQueriesMaxDepth).
            if (NeedsNestedProjection(configuration, sourceMemberType, destMemberType))
            {
                if (depth >= configuration.RecursiveQueriesMaxDepth)
                    return Expression.Constant(GetDefault(destMemberType), destMemberType);

                var nested = BuildMemberInit(configuration, sourceMemberType, destMemberType, current, parameters, membersToExpand, depth + 1);
                value = nested;
                if (nullGuard != null)
                    value = Expression.Condition(nullGuard, value, Expression.Constant(GetDefault(destMemberType), destMemberType));
                return value;
            }

            // Scalar / assignable: coerce where needed.
            if (destMemberType != sourceMemberType)
            {
                if (destMemberType.IsAssignableFrom(sourceMemberType))
                    value = Expression.Convert(current, destMemberType);
                else
                {
                    // Numeric/primitive conversions the provider can translate.
                    try { value = Expression.Convert(current, destMemberType); }
                    catch { return null; }
                }
            }

            if (nullGuard != null && (!destMemberType.IsValueType || Nullable.GetUnderlyingType(destMemberType) != null))
                value = Expression.Condition(nullGuard, value, Expression.Constant(GetDefault(destMemberType), destMemberType));

            return value;
        }

        private static Expression MakeMemberAccess(Expression target, MemberInfo member)
        {
            if (member is PropertyInfo pi) return Expression.Property(target, pi);
            if (member is FieldInfo fi) return Expression.Field(target, fi);
            throw new MapItMappingException($"Member {member.Name} cannot be projected.");
        }

        /// <summary>
        /// Builds <c>source.Select(e =&gt; new DestElement {{ ... }})</c> and materializes it to the
        /// destination collection type (array, List&lt;T&gt;, or IEnumerable&lt;T&gt;).
        /// </summary>
        private static Expression BuildCollectionProjection(MapperConfiguration configuration, Expression sourceCollection,
            Type srcElement, Type destElement, Type destMemberType, IDictionary<string, object> parameters, HashSet<string> membersToExpand, int depth)
        {
            var elementParam = Expression.Parameter(srcElement, "e");
            var elementBody = BuildMemberInit(configuration, srcElement, destElement, elementParam, parameters, membersToExpand, depth + 1);
            var elementLambda = Expression.Lambda(elementBody, elementParam);

            // Enumerable.Select<srcElement, destElement>(source, lambda)
            var selectMethod = typeof(System.Linq.Enumerable).GetMethods()
                .First(m => m.Name == "Select" && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2)
                .MakeGenericMethod(srcElement, destElement);
            Expression projected = Expression.Call(selectMethod, sourceCollection, elementLambda);

            // Materialize to the destination shape.
            if (destMemberType.IsArray)
            {
                var toArray = typeof(System.Linq.Enumerable).GetMethod("ToArray").MakeGenericMethod(destElement);
                return Expression.Call(toArray, projected);
            }

            // IEnumerable<T> / IReadOnlyList<T> / ICollection<T> can accept a List<T>.
            var toList = typeof(System.Linq.Enumerable).GetMethod("ToList").MakeGenericMethod(destElement);
            Expression list = Expression.Call(toList, projected);

            if (destMemberType.IsAssignableFrom(typeof(System.Collections.Generic.List<>).MakeGenericType(destElement)))
                return list;

            // Otherwise leave as the enumerable Select (providers often accept this).
            return Expression.Convert(projected, destMemberType);
        }

        private static bool NeedsNestedProjection(MapperConfiguration configuration, Type sourceType, Type destType)
        {
            if (sourceType == destType) return false;
            if (IsSimple(sourceType) || IsSimple(destType)) return false;
            if (ReflectionHelper.IsCollection(sourceType) || ReflectionHelper.IsCollection(destType)) return false; // collection projection out of scope here
            return configuration.FindTypeMapFor(sourceType, destType) != null;
        }

        private static bool IsSimple(Type type)
        {
            var t = ReflectionHelper.GetUnderlyingNullableType(type);
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
                || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid);
        }

        private static object GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        private static MapItMappingException Untranslatable(string member, Type sourceType, Type destType, string why)
        {
            var where = member == null ? "" : $" (member '{member}')";
            return new MapItMappingException(
                $"The map {sourceType.Name} -> {destType.Name}{where} uses {why} that cannot be projected to a query; evaluate in memory with Map instead.")
            { Types = new TypePair(sourceType, destType) };
        }
    }
}
