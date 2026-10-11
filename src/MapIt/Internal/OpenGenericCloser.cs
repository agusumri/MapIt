using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Closes open-generic map definitions into concrete type maps on demand (BEHAVIOR §2 step 4),
    /// replaying each recorded configuration step against the closed reflection metadata.
    /// </summary>
    internal static class OpenGenericCloser
    {
        public static TypeMap TryClose(MapperConfiguration configuration, IReadOnlyList<TypeMap> openMaps, TypePair requested)
        {
            if (requested.SourceType == null || requested.DestinationType == null) return null;

            foreach (var open in openMaps)
            {
                var closed = TryCloseOne(configuration, open, requested);
                if (closed != null) return closed;
            }
            return null;
        }

        private static TypeMap TryCloseOne(MapperConfiguration configuration, TypeMap open, TypePair requested)
        {
            var ogm = open.OpenGenericTypeMap;
            if (ogm == null) return null;

            // Each side is closed with its own type arguments, so Page<Order> -> PageDto<OrderDto>
            // works; a non-generic side reuses the other side's arguments.
            var sourceArgs = ArgumentsFor(open.SourceType, requested.SourceType);
            var destArgs = ArgumentsFor(open.DestinationType, requested.DestinationType);
            if (sourceArgs == null && destArgs == null) return null;
            sourceArgs = sourceArgs ?? destArgs;
            destArgs = destArgs ?? sourceArgs;
            var typeArgs = destArgs;

            Type closedSource, closedDest;
            try
            {
                closedSource = CloseType(open.SourceType, sourceArgs);
                closedDest = CloseType(open.DestinationType, destArgs);
            }
            catch (Exception ex)
            {
                throw new MapItMappingException(
                    $"Could not close the open-generic map {open.SourceType.Name} -> {open.DestinationType.Name} for {requested.SourceType.Name} -> {requested.DestinationType.Name}: {ex.Message}")
                { Types = requested };
            }

            // The closed pair must match the requested pair (modulo the source, which may be derived).
            if (closedDest != requested.DestinationType) return null;
            if (closedSource != requested.SourceType && !closedSource.IsAssignableFrom(requested.SourceType)) return null;

            var closedMap = new TypeMap(requested.SourceType, requested.DestinationType, open.ConfiguredMemberList);
            closedMap.MaxDepth = open.MaxDepth;
            closedMap.HasExplicitMaxDepth = open.HasExplicitMaxDepth;
            closedMap.PreserveReferences = open.PreserveReferences;
            closedMap.IncludeAllDerived = open.IncludeAllDerived;
            closedMap.Profile = open.Profile;

            // Destination-type override.
            if (ogm.DestinationTypeOverrideDefinition != null)
                closedMap.DestinationTypeOverride = CloseType(ogm.DestinationTypeOverrideDefinition, typeArgs);
            else if (open.DestinationTypeOverride != null)
                closedMap.DestinationTypeOverride = open.DestinationTypeOverride;

            // Included derived/base pairs closed with each side's arguments.
            foreach (var inc in ogm.IncludedDerived)
                closedMap.IncludedDerivedTypes.Add(new TypePair(CloseType(inc.Source, sourceArgs), CloseType(inc.Destination, destArgs)));
            foreach (var inc in ogm.IncludedBase)
                closedMap.IncludedBaseTypes.Add(new TypePair(CloseType(inc.Source, sourceArgs), CloseType(inc.Destination, destArgs)));

            // Replay the recorded steps.
            foreach (var step in ogm.Steps)
                ReplayStep(configuration, closedMap, step, requested);

            // Convention discovery for the remaining members.
            configuration.RunConventionDiscovery(closedMap);
            configuration.ApplySelfReferenceToClosedMap(closedMap);
            closedMap.Seal();
            return closedMap;
        }

        /// <summary>
        /// Type arguments of <paramref name="requested"/> when it is a constructed instance of the open
        /// definition <paramref name="openDefinition"/>; null when that side is not an open generic match.
        /// </summary>
        private static Type[] ArgumentsFor(Type openDefinition, Type requested)
        {
            if (openDefinition == null || requested == null) return null;
            if (!openDefinition.IsGenericTypeDefinition || !requested.IsGenericType) return null;
            return requested.GetGenericTypeDefinition() == openDefinition ? requested.GetGenericArguments() : null;
        }

        private static Type CloseType(Type openOrClosed, Type[] typeArgs)
        {
            if (openOrClosed == null) return null;
            if (openOrClosed.IsGenericTypeDefinition)
                return openOrClosed.MakeGenericType(typeArgs);
            return openOrClosed;
        }

        private static void ReplayStep(MapperConfiguration configuration, TypeMap closedMap, OpenMapConfigStep step, TypePair requested)
        {
            switch (step)
            {
                case OpenMemberStep m:
                    ReplayMember(closedMap, m, requested);
                    break;
                case OpenPathStep p:
                    ReplayPath(closedMap, p, requested);
                    break;
                case OpenCtorParamStep c:
                    ReplayCtorParam(closedMap, c, requested);
                    break;
            }
        }

        private static void ReplayMember(TypeMap closedMap, OpenMemberStep step, TypePair requested)
        {
            var prop = closedMap.DestinationType.GetProperty(step.DestinationMemberName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                throw new MapItMappingException(
                    $"Closing open map for {requested.SourceType.Name} -> {requested.DestinationType.Name}: destination member '{step.DestinationMemberName}' not found.")
                { Types = requested };

            var pm = closedMap.FindOrCreatePropertyMapFor(prop, prop.PropertyType);
            pm.IsExplicitlyConfigured = true;
            ApplyRecipe(closedMap, pm, step.Recipe, requested, step.DestinationMemberName);
        }

        private static void ReplayPath(TypeMap closedMap, OpenPathStep step, TypePair requested)
        {
            var members = new List<MemberInfo>();
            var type = closedMap.DestinationType;
            foreach (var seg in step.PathSegmentNames)
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, seg);
                if (member == null)
                    throw new MapItMappingException(
                        $"Closing open map for {requested.SourceType.Name} -> {requested.DestinationType.Name}: path segment '{seg}' not found.")
                    { Types = requested };
                members.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            var path = new MemberPath(members.ToArray());
            var pathMap = closedMap.FindOrCreatePathMapFor(null, path, closedMap);
            pathMap.IsExplicitlyConfigured = true;
            ApplyRecipeToMember(closedMap, pathMap, step.Recipe, requested, step.PathSegmentNames.Last());
        }

        private static void ReplayCtorParam(TypeMap closedMap, OpenCtorParamStep step, TypePair requested)
        {
            var cpm = closedMap.ConstructorParameterMaps.FirstOrDefault(x => string.Equals(x.DestinationName, step.CtorParamName, StringComparison.OrdinalIgnoreCase));
            if (cpm == null)
            {
                cpm = new ConstructorParameterMap { DestinationName = step.CtorParamName, SourceType = closedMap.SourceType, TypeMap = closedMap, IsExplicitlyConfigured = true };
                closedMap.ConstructorParameterMaps.Add(cpm);
            }
            ApplyRecipeToMember(closedMap, cpm, step.Recipe, requested, step.CtorParamName);
        }

        private static void ApplyRecipe(TypeMap closedMap, PropertyMap pm, OpenValueRecipe recipe, TypePair requested, string memberName)
        {
            ApplyRecipeToMember(closedMap, pm, recipe, requested, memberName);
            if (recipe.MappingOrder.HasValue) pm.MappingOrder = recipe.MappingOrder;
        }

        private static void ApplyRecipeToMember(TypeMap closedMap, MemberMap mm, OpenValueRecipe recipe, TypePair requested, string memberName)
        {
            if (recipe == null) return;
            if (recipe.Ignore) { mm.Ignored = true; return; }
            if (recipe.NullSubstitute != null) mm.NullSubstitute = recipe.NullSubstitute;
            if (recipe.AllowNull.HasValue) mm.AllowNull = recipe.AllowNull;
            if (recipe.ExplicitExpansion) mm.IsExplicitExpansion = true;
            if (recipe.UseDestinationValue.HasValue) mm.UseDestinationValue = recipe.UseDestinationValue;

            if (recipe.SourceMemberNames != null && recipe.SourceMemberNames.Length > 0)
            {
                var chain = ResolveSourceChain(closedMap.SourceType, recipe.SourceMemberNames, requested);
                mm.SourceMembers = chain;
            }

            if (recipe.ResolverType != null)
            {
                var resolverType = CloseIfOpen(recipe.ResolverType, closedMap);
                mm.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                {
                    var resolver = ctx.ServiceCtor != null ? ctx.ServiceCtor(resolverType) : Activator.CreateInstance(resolverType);
                    return InvokeResolver(resolver, s, d, dm, ctx);
                });
            }
            else if (recipe.ConverterType != null)
            {
                var converterType = CloseIfOpen(recipe.ConverterType, closedMap);
                mm.Resolver = new ValueConverterMember((s, d, sm, dm, ctx) =>
                {
                    var converter = ctx.ServiceCtor != null ? ctx.ServiceCtor(converterType) : Activator.CreateInstance(converterType);
                    return InvokeConverter(converter, sm, ctx);
                });
            }
            else if (recipe.RawExpression != null)
            {
                var rewritten = ExpressionRebinder.Rebind(recipe.RawExpression, closedMap.SourceType);
                var func = rewritten.Compile();
                mm.SourceMembers = TryExtractSimpleChain(rewritten, closedMap.SourceType);
                if (mm.SourceMembers == null)
                    mm.Resolver = new ValueResolverMember((s, d, dm, ctx) => func.DynamicInvoke(s));
            }
        }

        private static Type CloseIfOpen(Type t, TypeMap closedMap)
        {
            if (!t.IsGenericTypeDefinition) return t;
            // Close a resolver/converter definition with the destination's type args as a best effort.
            var args = closedMap.DestinationType.IsGenericType ? closedMap.DestinationType.GetGenericArguments() : Array.Empty<Type>();
            try { return t.MakeGenericType(args); }
            catch { return t; }
        }

        private static MemberInfo[] ResolveSourceChain(Type sourceType, string[] names, TypePair requested)
        {
            var chain = new List<MemberInfo>();
            var type = sourceType;
            foreach (var name in names)
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, name);
                if (member == null)
                    throw new MapItMappingException(
                        $"Closing open map for {requested.SourceType.Name} -> {requested.DestinationType.Name}: source member '{name}' not found.")
                    { Types = requested };
                chain.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            return chain.ToArray();
        }

        private static MemberInfo[] TryExtractSimpleChain(LambdaExpression expr, Type sourceType)
        {
            var body = expr.Body;
            if (body is UnaryExpression ue) body = ue.Operand;
            var members = new List<MemberInfo>();
            var node = body;
            while (node is MemberExpression me)
            {
                members.Insert(0, me.Member);
                node = me.Expression;
            }
            if (node is ParameterExpression && members.Count > 0)
            {
                // Rebind members onto the closed source type by name.
                var rebinded = new List<MemberInfo>();
                var type = sourceType;
                foreach (var m in members)
                {
                    var member = ReflectionHelper.GetFieldOrProperty(type, m.Name);
                    if (member == null) return null;
                    rebinded.Add(member);
                    type = ReflectionHelper.GetMemberType(member);
                }
                return rebinded.ToArray();
            }
            return null;
        }

        private static object InvokeResolver(object resolver, object s, object d, object dm, ResolutionContext ctx)
        {
            var iface = resolver.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueResolver<,,>));
            if (iface != null)
            {
                var method = iface.GetMethod("Resolve");
                return method.Invoke(resolver, new[] { s, d, dm, ctx });
            }
            throw new MapItMappingException($"Resolver {resolver.GetType().Name} does not implement IValueResolver<,,>.");
        }

        private static object InvokeConverter(object converter, object sm, ResolutionContext ctx)
        {
            var iface = converter.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueConverter<,>));
            if (iface != null)
            {
                var method = iface.GetMethod("Convert");
                return method.Invoke(converter, new[] { sm, ctx });
            }
            throw new MapItMappingException($"Converter {converter.GetType().Name} does not implement IValueConverter<,>.");
        }
    }

    /// <summary>Rewrites an open-definition lambda's parameter/member accesses onto a closed source type.</summary>
    internal sealed class ExpressionRebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _newParam;
        private readonly ParameterExpression _oldParam;

        private ExpressionRebinder(ParameterExpression oldParam, ParameterExpression newParam)
        {
            _oldParam = oldParam;
            _newParam = newParam;
        }

        public static LambdaExpression Rebind(LambdaExpression original, Type closedSourceType)
        {
            var oldParam = original.Parameters[0];
            if (oldParam.Type.IsAssignableFrom(closedSourceType) || closedSourceType.IsAssignableFrom(oldParam.Type) || oldParam.Type == closedSourceType)
            {
                var newParam = Expression.Parameter(closedSourceType, oldParam.Name);
                var rebinder = new ExpressionRebinder(oldParam, newParam);
                var body = rebinder.Visit(original.Body);
                return Expression.Lambda(body, newParam);
            }
            return original;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _oldParam ? _newParam : base.VisitParameter(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            var expr = Visit(node.Expression);
            if (expr != null && expr.Type != node.Member.DeclaringType)
            {
                var member = ReflectionHelper.GetFieldOrProperty(expr.Type, node.Member.Name);
                if (member is PropertyInfo pi) return Expression.Property(expr, pi);
                if (member is FieldInfo fi) return Expression.Field(expr, fi);
            }
            return base.VisitMember(node);
        }
    }
}
