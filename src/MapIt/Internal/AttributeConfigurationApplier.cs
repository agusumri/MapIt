using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Applies member and type attributes to type maps at seal time (BEHAVIOR §8).
    /// Attributes are treated as data carriers; the applier reads them and mutates the member maps.
    /// </summary>
    internal static class AttributeConfigurationApplier
    {
        public static void Apply(IEnumerable<TypeMap> maps, MapperConfigurationExpression config)
        {
            foreach (var typeMap in maps)
            {
                if (typeMap.IsOpenGeneric) continue;
                if (typeMap.DestinationType == null || typeMap.SourceType == null) continue;
                ApplyToMap(typeMap);
            }
        }

        private static void ApplyToMap(TypeMap typeMap)
        {
            foreach (var destProp in typeMap.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                var attrs = destProp.GetCustomAttributes(inherit: true).ToArray();
                if (attrs.Length == 0) continue;

                PropertyMap pm = null;
                PropertyMap EnsurePm()
                {
                    if (pm == null)
                    {
                        pm = typeMap.FindOrCreatePropertyMapFor(destProp, destProp.PropertyType);
                        pm.IsExplicitlyConfigured = true;
                    }
                    return pm;
                }

                foreach (var attr in attrs)
                {
                    switch (attr)
                    {
                        case IgnoreAttribute _:
                            EnsurePm().Ignored = true;
                            break;
                        case SourceMemberAttribute sm:
                            {
                                var chain = ResolveChain(typeMap.SourceType, sm.Name);
                                if (chain == null)
                                    throw Config($"[SourceMember(\"{sm.Name}\")] on {typeMap.DestinationType.Name}.{destProp.Name} does not resolve on {typeMap.SourceType.Name}.", typeMap);
                                var m = EnsurePm();
                                m.SourceMembers = chain;
                                m.Ignored = false;
                                break;
                            }
                        case NullSubstituteAttribute ns:
                            EnsurePm().NullSubstitute = ns.Value;
                            break;
                        case MapAtRuntimeAttribute _:
                            EnsurePm().MapAtRuntime = true;   // recorded, no runtime effect (§11)
                            break;
                        case UseExistingValueAttribute _:
                            EnsurePm().UseDestinationValue = true;
                            break;
                        case MappingOrderAttribute mo:
                            EnsurePm().MappingOrder = mo.Value;
                            break;
                        case ValueResolverAttribute vr:
                            {
                                var resolverType = vr.Type;
                                EnsureImplements(resolverType, typeof(IValueResolver<,,>), typeMap, destProp, "ValueResolver");
                                var rm = EnsurePm();
                                rm.Ignored = false;
                                rm.Resolver = new ValueResolverMember((s, d, dm, ctx) =>
                                {
                                    var resolver = ctx.ServiceCtor != null ? ctx.ServiceCtor(resolverType) : Activator.CreateInstance(resolverType);
                                    return InvokeResolver(resolver, s, d, dm, ctx);
                                });
                                break;
                            }
                        case ValueConverterAttribute vc:
                            {
                                var converterType = vc.Type;
                                EnsureImplements(converterType, typeof(IValueConverter<,>), typeMap, destProp, "ValueConverter");
                                var cm = EnsurePm();
                                cm.Ignored = false;
                                // Default converter source member = the member of the same name on the source.
                                if (cm.SourceMembers == null)
                                {
                                    var srcMember = ReflectionHelper.GetFieldOrProperty(typeMap.SourceType, destProp.Name);
                                    if (srcMember != null) cm.SourceMembers = new[] { srcMember };
                                }
                                cm.Resolver = new ValueConverterMember((s, d, srcm, dm, ctx) =>
                                {
                                    var converter = ctx.ServiceCtor != null ? ctx.ServiceCtor(converterType) : Activator.CreateInstance(converterType);
                                    return InvokeConverter(converter, srcm, ctx);
                                });
                                break;
                            }
                    }
                }
            }
        }

        private static MemberInfo[] ResolveChain(Type sourceType, string dottedName)
        {
            if (string.IsNullOrEmpty(dottedName)) return null;
            var chain = new List<MemberInfo>();
            var type = sourceType;
            foreach (var name in dottedName.Split('.'))
            {
                var member = ReflectionHelper.GetFieldOrProperty(type, name);
                if (member == null) return null;
                chain.Add(member);
                type = ReflectionHelper.GetMemberType(member);
            }
            return chain.ToArray();
        }

        private static void EnsureImplements(Type type, Type openInterface, TypeMap typeMap, MemberInfo member, string kind)
        {
            if (type == null)
                throw Config($"[{kind}] on {typeMap.DestinationType.Name}.{member.Name} has a null type.", typeMap);
            var implements = type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);
            if (!implements)
                throw Config($"[{kind}] type {type.Name} on {typeMap.DestinationType.Name}.{member.Name} does not implement {openInterface.Name}.", typeMap);
        }

        private static object InvokeResolver(object resolver, object s, object d, object dm, ResolutionContext ctx)
        {
            var iface = resolver.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueResolver<,,>));
            return iface.GetMethod("Resolve").Invoke(resolver, new[] { s, d, dm, ctx });
        }

        private static object InvokeConverter(object converter, object sm, ResolutionContext ctx)
        {
            var iface = converter.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueConverter<,>));
            return iface.GetMethod("Convert").Invoke(converter, new[] { sm, ctx });
        }

        private static MapItConfigurationException Config(string message, TypeMap typeMap)
            => new MapItConfigurationException(message) { Types = typeMap.Types };
    }
}
