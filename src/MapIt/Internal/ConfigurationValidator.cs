using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Validates type maps and collects all problems in one pass (BEHAVIOR §9): destination/source
    /// member coverage per MemberList, constructibility, and nested member-pair resolvability.
    /// </summary>
    internal static class ConfigurationValidator
    {
        public static List<TypeMapConfigErrors> Validate(IEnumerable<TypeMap> maps, IGlobalConfiguration configuration, MapperConfigurationExpression config)
        {
            var errors = new List<TypeMapConfigErrors>();
            foreach (var typeMap in maps)
                ValidateOne(typeMap, configuration, config, errors);

            // User-supplied validators.
            foreach (var validator in config.Validators)
            {
                foreach (var typeMap in maps)
                {
                    try { validator(new ValidationContext(typeMap.Types, null, typeMap, null)); }
                    catch (Exception ex)
                    {
                        errors.Add(new TypeMapConfigErrors { TypeMap = typeMap, CanConstruct = true, UnmappedPropertyNames = new[] { ex.Message } });
                    }
                }
            }
            return errors;
        }

        private static void ValidateOne(TypeMap typeMap, IGlobalConfiguration configuration, MapperConfigurationExpression config, List<TypeMapConfigErrors> errors)
        {
            if (typeMap.SourceType == null || typeMap.DestinationType == null) return;

            var unmapped = new string[0];
            var nested = new List<string>();
            bool canConstruct = true;

            bool isConverter = typeMap.TypeConverter != null;

            if (!isConverter)
            {
                // Member coverage checks per MemberList.
                if (typeMap.ConfiguredMemberList == MemberList.Destination)
                    unmapped = GetUnmappedDestinationMembers(typeMap, config);
                else if (typeMap.ConfiguredMemberList == MemberList.Source)
                    unmapped = GetUnmappedSourceMembers(typeMap);
                // MemberList.None -> no member checks.

                // Nested member-pair resolvability.
                CollectUnmappableNestedPairs(typeMap, configuration, nested, new HashSet<TypePair>());

                // Constructibility.
                if (!typeMap.DisableConstructorValidation && typeMap.CustomCtor == null && !typeMap.ConstructViaServiceLocator)
                    canConstruct = CanConstruct(typeMap);
            }

            if (unmapped.Length > 0 || nested.Count > 0 || !canConstruct)
            {
                errors.Add(new TypeMapConfigErrors
                {
                    TypeMap = typeMap,
                    UnmappedPropertyNames = unmapped,
                    UnmappableNestedPairs = nested.Count > 0 ? nested.ToArray() : null,
                    CanConstruct = canConstruct
                });
            }
        }

        private static string[] GetUnmappedDestinationMembers(TypeMap typeMap, MapperConfigurationExpression config)
        {
            var unmapped = new List<string>();
            var mappedNames = new HashSet<string>(
                typeMap.PropertyMaps.Where(p => p.IsMapped && !p.Ignored).Select(p => p.DestinationName), StringComparer.Ordinal);
            var ctorFed = new HashSet<string>(typeMap.ConstructorParameterMaps.Select(c => c.DestinationName), StringComparer.OrdinalIgnoreCase);
            var sourceType = typeMap.SourceType;

            foreach (var prop in typeMap.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                if (mappedNames.Contains(prop.Name)) continue;
                if (ctorFed.Contains(prop.Name)) continue;
                // Explicitly ignored (user ForMember(...).Ignore() or [Ignore]) is fine; a
                // convention-ignored member (no source found) is unmapped.
                if (typeMap.PropertyMaps.Any(p => p.DestinationName == prop.Name && p.Ignored && p.IsExplicitlyConfigured)) continue;
                if (config.GlobalIgnores.Any(ig => prop.Name.StartsWith(ig, StringComparison.Ordinal))) continue;
                // Convention match on the source?
                if (ReflectionHelper.GetFieldOrProperty(sourceType, prop.Name) != null) continue;
                unmapped.Add(prop.Name);
            }
            return unmapped.ToArray();
        }

        private static string[] GetUnmappedSourceMembers(TypeMap typeMap)
        {
            var unmapped = new List<string>();
            var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pm in typeMap.PropertyMaps.Where(p => !p.Ignored && p.SourceMembers != null))
                foreach (var sm in pm.SourceMembers)
                    consumed.Add(sm.Name);

            var ignoredSources = typeMap.SourceMemberConfigs.Values.Where(c => c.IsIgnored()).Select(c => c.SourceMember.Name);
            var ignored = new HashSet<string>(ignoredSources, StringComparer.OrdinalIgnoreCase);

            foreach (var prop in typeMap.SourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (consumed.Contains(prop.Name) || ignored.Contains(prop.Name)) continue;
                // Convention: a destination member of the same name consumes it.
                if (ReflectionHelper.GetFieldOrProperty(typeMap.DestinationType, prop.Name) != null) continue;
                unmapped.Add(prop.Name);
            }
            return unmapped.ToArray();
        }

        private static void CollectUnmappableNestedPairs(TypeMap typeMap, IGlobalConfiguration configuration, List<string> nested, HashSet<TypePair> visited)
        {
            foreach (var pm in typeMap.PropertyMaps.Where(p => !p.Ignored))
            {
                var srcChain = pm.SourceMembers;
                if (srcChain == null || srcChain.Length == 0) continue;
                if (pm.Resolver != null) continue;

                var srcType = ReflectionHelper.GetMemberType(srcChain[srcChain.Length - 1]);
                var destType = pm.DestinationType;
                if (srcType == null || destType == null) continue;

                var srcElem = ReflectionHelper.GetEnumerableElementType(srcType);
                var destElem = ReflectionHelper.GetEnumerableElementType(destType);
                if (srcElem != null && destElem != null) { srcType = srcElem; destType = destElem; }

                if (!IsComplex(srcType) || !IsComplex(destType)) continue;
                if (srcType == destType) continue;

                var pair = new TypePair(srcType, destType);
                if (!visited.Add(pair)) continue;

                var nestedMap = configuration.FindTypeMapFor(srcType, destType);
                if (nestedMap == null)
                    nested.Add($"{srcType.Name} -> {destType.Name}");
            }
        }

        private static bool IsComplex(Type type)
        {
            if (type == null) return false;
            var t = ReflectionHelper.GetUnderlyingNullableType(type);
            if (t.IsPrimitive || t.IsEnum) return false;
            if (t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
                || t == typeof(TimeSpan) || t == typeof(Guid)) return false;
            return t.IsClass || (t.IsValueType && !t.IsPrimitive);
        }

        private static bool CanConstruct(TypeMap typeMap)
        {
            var dest = typeMap.DestinationType;
            if (dest.IsValueType) return true;
            if (dest.IsAbstract || dest.IsInterface) return false;
            if (dest.GetConstructor(Type.EmptyTypes) != null) return true;
            // Any constructor whose parameters can be fed by ctor-param maps or source members.
            foreach (var ctor in dest.GetConstructors())
            {
                var ps = ctor.GetParameters();
                bool all = ps.All(p =>
                    typeMap.ConstructorParameterMaps.Any(m => string.Equals(m.DestinationName, p.Name, StringComparison.OrdinalIgnoreCase))
                    || ReflectionHelper.GetFieldOrProperty(typeMap.SourceType, p.Name) != null
                    || p.HasDefaultValue);
                if (all) return true;
            }
            return false;
        }
    }
}
