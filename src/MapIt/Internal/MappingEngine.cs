using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// The core mapping engine. Performs reflection-based runtime mapping using the configured type maps.
    /// </summary>
    public static class MappingEngine
    {
        /// <summary>
        /// Maps a source object to a destination of the given type, using the provided configuration.
        /// </summary>
        public static object Map(
            IGlobalConfiguration configuration,
            object source,
            object destination,
            Type sourceType,
            Type destinationType,
            ResolutionContext context,
            MapRequest mapRequest)
        {
            // S1 depth cap: increment on entry, decrement on exit.
            context.CurrentDepth++;
            try
            {
                if (context.CurrentDepth > configuration.MaxGraphDepth)
                    throw new MapItMappingException(
                        $"Maximum mapping depth of {configuration.MaxGraphDepth} exceeded mapping {sourceType?.Name} to {destinationType?.Name}. This usually indicates a self-referential graph without a MaxDepth or PreserveReferences setting.")
                    { Types = new TypePair(sourceType, destinationType) };

                return MapCore(configuration, source, destination, sourceType, destinationType, context, mapRequest);
            }
            finally
            {
                context.CurrentDepth--;
            }
        }

        private static object MapCore(
            IGlobalConfiguration configuration,
            object source,
            object destination,
            Type sourceType,
            Type destinationType,
            ResolutionContext context,
            MapRequest mapRequest)
        {
            // 1. Type converter (ConvertUsing) takes priority
            var typeMap = configuration.FindTypeMapFor(sourceType, destinationType);
            if (typeMap?.TypeConverter != null)
            {
                return typeMap.TypeConverter.Convert(source, destination, context);
            }

            // 2. Null source
            if (source == null)
            {
                // Collection/dictionary destinations route to the collection null rule.
                if (ReflectionHelper.IsCollection(destinationType) || destinationType.IsDictionaryType())
                {
                    var ownerMap = typeMap ?? mapRequest.MemberMap?.TypeMap;
                    return AllowsNullCollections(configuration, mapRequest.MemberMap, sourceType, destinationType)
                        ? (object)null
                        : CreateEmptyCollection(destinationType);
                }
                return AllowNullDestinationValues(configuration, sourceType, destinationType) ? null : CreateNew(destinationType);
            }

            // 3. Preserve references (cycle handling)
            if (typeMap?.PreserveReferences == true)
            {
                var key = new ContextCacheKey(source, destinationType);
                if (context.InstanceCache.TryGetValue(key, out var cached))
                    return cached;
            }

            // 4. Same type.
            if (sourceType == destinationType)
            {
                // §6: a configured, member-carrying T->T map maps a NEW instance; a convention-only
                // or absent map passes the source through. Clone forces construction via the
                // ForceConstruct context flag (MapIt deep-clone capability).
                if (typeMap != null && (typeMap.HasConfiguredMembersOrCtorOrConverter || context.ForceConstruct))
                    return MapObject(configuration, source, destination, sourceType, destinationType, typeMap, context);
                var result = source;
                CacheReference(typeMap, source, destinationType, result, context);
                return result;
            }

            // 4b. Dynamic / ExpandoObject mapping (when the other side is a POCO).
            if (IsDynamic(destinationType) && !IsDynamic(sourceType) && !ReflectionHelper.IsCollection(sourceType))
            {
                return MapToDynamic(configuration, source, sourceType, destinationType, context);
            }
            if (IsDynamic(sourceType) && !IsDynamic(destinationType) && !ReflectionHelper.IsCollection(destinationType)
                && !destinationType.IsDictionaryType() && IsComplex(destinationType))
            {
                return MapFromDynamic(configuration, source, destination, destinationType, context);
            }

            // 5. Collections
            if (ReflectionHelper.IsCollection(sourceType) || ReflectionHelper.IsCollection(destinationType))
            {
                return MapCollection(configuration, source, destination, sourceType, destinationType, context);
            }

            // 6. Dictionaries
            if (sourceType.IsDictionaryType() || destinationType.IsDictionaryType())
            {
                return MapDictionary(configuration, source, destination, sourceType, destinationType, context);
            }

            // A configured map that carries real member/ctor/converter config always wins.
            var mapWins = typeMap != null && typeMap.HasConfiguredMembersOrCtorOrConverter;

            // 7. Enum mappings (when no member-carrying map wins) — after nullable unwrap.
            if (!mapWins && IsEnumInvolved(sourceType, destinationType))
            {
                return MapEnum(source, sourceType, destinationType, context, typeMap?.EnumValueOverrides);
            }

            // 8. Primitive / string / convertible mappings
            if (!mapWins && IsPrimitiveConvertible(sourceType, destinationType))
            {
                return ConvertPrimitive(source, sourceType, destinationType);
            }

            // S8: both complex and no map resolves -> throw instead of silently producing garbage.
            if (typeMap == null && IsComplex(sourceType) && IsComplex(destinationType))
            {
                throw TypeMap.MissingMapException(sourceType, destinationType);
            }

            // 9. Object-to-object mapping (default)
            return MapObject(configuration, source, destination, sourceType, destinationType, typeMap, context);
        }

        private static bool IsEnumInvolved(Type sourceType, Type destinationType)
        {
            var s = ReflectionHelper.GetUnderlyingNullableType(sourceType);
            var d = ReflectionHelper.GetUnderlyingNullableType(destinationType);
            return s.IsEnum || d.IsEnum;
        }

        private static bool IsComplex(Type type)
        {
            if (type == null) return false;
            var t = ReflectionHelper.GetUnderlyingNullableType(type);
            if (t.IsPrimitive || t.IsEnum) return false;
            if (t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
                || t == typeof(TimeSpan) || t == typeof(Guid)) return false;
            if (ReflectionHelper.IsCollection(t) || t.IsDictionaryType()) return false;
            return t.IsClass || (t.IsValueType && !t.IsPrimitive);
        }

        /// <summary>Maps an object to another object using member-by-member reflection.</summary>
        private static object MapObject(
            IGlobalConfiguration configuration,
            object source,
            object destination,
            Type sourceType,
            Type destinationType,
            TypeMap typeMap,
            ResolutionContext context)
        {
            // S3 MaxDepth truncation: at the (n+1)-th occurrence of this map on the current path,
            // return the member default instead of recursing further.
            if (typeMap != null && typeMap.MaxDepth != int.MaxValue)
            {
                var occurrence = context.EnterTypeMap(typeMap);
                if (occurrence > typeMap.MaxDepth)
                {
                    context.ExitTypeMap(typeMap);
                    return destination ?? CreateNew(destinationType);
                }
            }
            try
            {
            // Ensure a destination instance exists
            var dest = destination;
            if (dest == null)
            {
                // Custom ctor takes priority over default construction
                if (typeMap?.CustomCtor != null)
                {
                    dest = typeMap.CustomCtor.Ctor(source, context);
                }
                else if (typeMap?.ConstructorParameterMaps != null && typeMap.ConstructorParameterMaps.Count > 0)
                {
                    dest = ConstructWithParameters(configuration, source, typeMap, context);
                }
                else
                {
                    dest = CreateNew(destinationType);
                    if (dest == null)
                        throw new MapItMappingException($"Unable to create an instance of {destinationType.Name}.")
                        { Types = new TypePair(sourceType, destinationType), TypeMap = typeMap };
                }
            }

            // S4 PreserveReferences: register the destination immediately after construction.
            CacheReference(typeMap, source, destinationType, dest, context);

            // Before-map actions
            RunActions(typeMap?.BeforeMapActions, source, dest, context, sourceType, destinationType);

            // Map each configured member
            if (typeMap != null)
            {
                foreach (var propMap in typeMap.PropertyMaps.OfType<PropertyMap>().OrderBy(p => p.MappingOrder ?? 0))
                {
                    if (propMap.Ignored) continue;
                    MapProperty(configuration, source, dest, propMap, context);
                }

                // Path maps
                foreach (var pathMap in typeMap.PathMaps.OfType<PathMap>())
                {
                    if (pathMap.Ignored) continue;
                    MapPath(configuration, source, dest, pathMap, context);
                }
            }

            // After-map actions
            RunActions(typeMap?.AfterMapActions, source, dest, context, sourceType, destinationType);

            return dest;
            }
            finally
            {
                if (typeMap != null && typeMap.MaxDepth != int.MaxValue)
                    context.ExitTypeMap(typeMap);
            }
        }

        /// <summary>Constructs a destination using explicit ctor-parameter maps.</summary>
        private static object ConstructWithParameters(
            IGlobalConfiguration configuration, object source, TypeMap typeMap, ResolutionContext context)
        {
            var destinationType = typeMap.DestinationType;
            var ctors = destinationType.GetConstructors();
            foreach (var ctor in ctors.OrderByDescending(c => c.GetParameters().Length))
            {
                var ps = ctor.GetParameters();
                var args = new object[ps.Length];
                bool ok = true;
                for (var i = 0; i < ps.Length; i++)
                {
                    var cpm = typeMap.ConstructorParameterMaps.FirstOrDefault(m => string.Equals(m.DestinationName, ps[i].Name, StringComparison.OrdinalIgnoreCase));
                    if (cpm != null && cpm.SourceMembers != null && cpm.SourceMembers.Length > 0)
                    {
                        var val = GetChainValue(source, cpm.SourceMembers, out _);
                        args[i] = CoerceArg(configuration, val, ps[i].ParameterType, context, cpm);
                    }
                    else if (cpm != null && cpm.Resolver != null)
                    {
                        var val = cpm.Resolver.Resolve(source, null, null, context);
                        args[i] = CoerceArg(configuration, val, ps[i].ParameterType, context, cpm);
                    }
                    else
                    {
                        // Match a source member by parameter name.
                        var srcMember = MatchSourceMember(source, typeMap.SourceType, ps[i].Name);
                        if (srcMember != null && srcMember.Length > 0)
                        {
                            var val = GetChainValue(source, srcMember, out _);
                            args[i] = CoerceArg(configuration, val, ps[i].ParameterType, context, null);
                        }
                        else if (ps[i].HasDefaultValue)
                            args[i] = ps[i].DefaultValue;
                        else if (ps[i].ParameterType.IsValueType)
                            args[i] = Activator.CreateInstance(ps[i].ParameterType);
                        else { args[i] = null; }
                    }
                }
                if (ok)
                    return ctor.Invoke(args);
            }
            return CreateNew(destinationType);
        }

        private static object CoerceArg(IGlobalConfiguration configuration, object value, Type targetType, ResolutionContext context, MemberMap memberMap)
        {
            if (value == null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            if (targetType.IsInstanceOfType(value)) return value;
            var srcType = value.GetType();
            return Map(configuration, value, null, srcType, targetType, context,
                new MapRequest(new TypePair(srcType, targetType), new TypePair(srcType, targetType), memberMap));
        }

        /// <summary>Maps a single property.</summary>
        private static void MapProperty(
            IGlobalConfiguration configuration,
            object source,
            object dest,
            PropertyMap propMap,
            ResolutionContext context)
        {
            // Async-resolver members are deferred to the MapAsync post-pass; during a plain sync
            // Map they are left at their default.
            if (propMap.AsyncResolver != null)
            {
                if (context.PendingAsync != null)
                    context.PendingAsync.Add(new PendingAsyncMember(source, dest, propMap));
                return;
            }

            var destMember = propMap.DestinationMember;
            var existingDestValue = ReflectionHelper.GetMemberValue(destMember, dest);

            // Pre-condition
            if (propMap.PreConditionDelegate != null)
            {
                if (!propMap.PreConditionDelegate(source, dest, null, existingDestValue, context))
                    return;
            }
            else if (propMap.PreCondition != null)
            {
                if (!InvokeCondition(propMap.PreCondition, source, dest, existingDestValue, context))
                    return;
            }

            // Resolve the source member chain value
            object sourceValue;
            bool hasSourceValue;
            if (propMap.Resolver != null)
            {
                // Resolve the source member chain first (for value converters that need it)
                object resolvedSourceMember = null;
                if (propMap.SourceMembers != null && propMap.SourceMembers.Length > 0)
                    resolvedSourceMember = GetChainValue(source, propMap.SourceMembers, out _);
                try
                {
                    sourceValue = propMap.Resolver.Resolve(source, dest, resolvedSourceMember ?? existingDestValue, context);
                }
                catch (MapItMappingException mex)
                {
                    if (mex.MemberMap == null) mex.MemberMap = propMap;
                    if (mex.TypeMap == null) mex.TypeMap = propMap.TypeMap;
                    throw;
                }
                catch (Exception ex)
                {
                    throw new MapItMappingException($"Error resolving member {propMap.DestinationName}.", ex)
                    { MemberMap = propMap, TypeMap = propMap.TypeMap, Types = propMap.Types() };
                }
                hasSourceValue = true;
            }
            else
            {
                // Use explicit source members, or match by convention
                var sourceChain = propMap.SourceMembers != null && propMap.SourceMembers.Length > 0
                    ? propMap.SourceMembers
                    : MatchSourceMember(source, sourceTypeOf(propMap), propMap.DestinationName);
                if (sourceChain != null)
                {
                    sourceValue = GetChainValue(source, sourceChain, out hasSourceValue);
                }
                else
                {
                    hasSourceValue = false;
                    sourceValue = null;
                }
            }

            // Condition
            if (propMap.ConditionDelegate != null)
            {
                if (!propMap.ConditionDelegate(source, dest, sourceValue, existingDestValue, context))
                    return;
            }
            else if (propMap.Condition != null)
            {
                if (!InvokeCondition(propMap.Condition, source, dest, sourceValue, context))
                    return;
            }

            // Use destination value
            if (propMap.UseDestinationValue == true)
            {
                return; // keep existing
            }

            // Did a source chain exist (explicit, resolver, or convention)? Distinguishes
            // "no source at all" from "source resolved to null" (§5 step 8).
            bool hasSourceChain = propMap.Resolver != null
                || (propMap.SourceMembers != null && propMap.SourceMembers.Length > 0)
                || MatchSourceMember(source, sourceTypeOf(propMap), propMap.DestinationName) != null;

            // §5 step 3: null substitute.
            if (sourceValue == null && propMap.NullSubstitute != null)
                sourceValue = propMap.NullSubstitute;

            // §5 step 4: value transformers (member order).
            sourceValue = ApplyTransformers(propMap, sourceValue);

            object mappedValue;
            if (propMap.DestinationType != null && sourceValue != null)
            {
                var srcType = sourceValue.GetType();
                // Same-type members short-circuit to the source reference, EXCEPT when a configured
                // self-map should deep-copy (clone) or the value is a mutable complex/collection
                // under ForceConstruct.
                bool sameTypeShortCircuit = propMap.DestinationType.IsInstanceOfType(sourceValue)
                    && srcType == propMap.DestinationType
                    && !(context.ForceConstruct && (IsComplex(srcType) || ReflectionHelper.IsCollection(srcType)));
                if (sameTypeShortCircuit)
                {
                    mappedValue = sourceValue;
                }
                else
                {
                    mappedValue = Map(
                        configuration,
                        sourceValue,
                        null,
                        srcType,
                        propMap.DestinationType,
                        context,
                        new MapRequest(new TypePair(srcType, propMap.DestinationType), new TypePair(srcType, propMap.DestinationType), propMap));
                }
            }
            else if (sourceValue == null)
            {
                if (!hasSourceChain)
                    return; // no source: leave the destination member untouched.
                mappedValue = ResolveNullDestination(configuration, propMap, existingDestValue);
                if (ReferenceEquals(mappedValue, KeepExistingSentinel))
                    return;
            }
            else
            {
                mappedValue = sourceValue;
            }

            // Set the destination member
            if (propMap.CanBeSet)
            {
                if (context.ChangeTracker != null)
                {
                    // Diff/patch: only write (and record) when the value actually changes.
                    if (!ValuesEqual(existingDestValue, mappedValue))
                    {
                        context.ChangeTracker.Add(new MemberChange(propMap.DestinationName, existingDestValue, mappedValue));
                        ReflectionHelper.SetMemberValue(destMember, dest, mappedValue);
                    }
                }
                else
                {
                    ReflectionHelper.SetMemberValue(destMember, dest, mappedValue);
                }
            }
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Equals(b);
        }

        private static readonly object KeepExistingSentinel = new object();

        /// <summary>Applies the member's value transformers in order.</summary>
        private static object ApplyTransformers(MemberMap memberMap, object value)
        {
            if (memberMap?.ValueTransformers == null) return value;
            foreach (var t in memberMap.ValueTransformers)
            {
                if (t.CompiledTransform != null && (t.ValueType == null || value == null || t.ValueType.IsInstanceOfType(value)))
                    value = t.CompiledTransform(value);
            }
            return value;
        }

        /// <summary>
        /// Resolves what a null source value becomes at the destination member (§5 step 8):
        /// collection rule, non-nullable struct default, or the AllowNullDestinationValues cascade.
        /// </summary>
        private static object ResolveNullDestination(IGlobalConfiguration configuration, PropertyMap propMap, object existingDestValue)
        {
            var memberType = propMap.DestinationType;
            if (ReflectionHelper.IsCollection(memberType) || memberType.IsDictionaryType())
            {
                return AllowsNullCollections(configuration, propMap, propMap.SourceType, memberType)
                    ? (object)null
                    : CreateEmptyCollection(memberType);
            }
            if (memberType.IsValueType && Nullable.GetUnderlyingType(memberType) == null)
            {
                return Activator.CreateInstance(memberType);   // non-nullable struct: default(T)
            }

            bool allowNull = propMap.TypeMap?.Profile?.AllowsNullDestinationValuesFor(propMap) ?? true;
            if (allowNull || Nullable.GetUnderlyingType(memberType) != null)
                return null;
            if (memberType == typeof(string))
                return string.Empty;

            var created = CreateNew(memberType);
            if (created == null)
                throw new MapItMappingException(
                    $"Unable to create an instance of {memberType.Name} for destination member {propMap.DestinationName}.")
                { Types = new TypePair(propMap.SourceType, memberType), TypeMap = propMap.TypeMap, MemberMap = propMap };
            return created;
        }

        /// <summary>Maps a nested path.</summary>
        private static void MapPath(
            IGlobalConfiguration configuration,
            object source,
            object dest,
            PathMap pathMap,
            ResolutionContext context)
        {
            // Navigate to the parent of the path and set the last member
            var members = pathMap.MemberPath.Members;
            if (members == null || members.Length == 0) return;

            object current = dest;
            for (var i = 0; i < members.Length - 1; i++)
            {
                var next = ReflectionHelper.GetMemberValue(members[i], current);
                if (next == null)
                {
                    // Create the intermediate destination object (BEHAVIOR §5 ForPath/unflattening).
                    var memberType = ReflectionHelper.GetMemberType(members[i]);
                    next = CreateNew(memberType);
                    if (next == null) return;
                    ReflectionHelper.SetMemberValue(members[i], current, next);
                }
                current = next;
            }
            var lastMember = members[members.Length - 1];

            // Get source value via source members
            object sourceValue = null;
            if (pathMap.SourceMembers != null && pathMap.SourceMembers.Length > 0)
                sourceValue = GetChainValue(source, pathMap.SourceMembers, out _);

            if (sourceValue == null) return;

            var mappedValue = Map(
                configuration,
                sourceValue,
                null,
                sourceValue.GetType(),
                ReflectionHelper.GetMemberType(lastMember),
                context,
                new MapRequest(
                    new TypePair(sourceValue.GetType(), ReflectionHelper.GetMemberType(lastMember)),
                    new TypePair(sourceValue.GetType(), ReflectionHelper.GetMemberType(lastMember)),
                    pathMap));

            ReflectionHelper.SetMemberValue(lastMember, current, mappedValue);
        }

        /// <summary>Maps a source collection to a destination collection.</summary>
        private static object MapCollection(
            IGlobalConfiguration configuration,
            object source,
            object destination,
            Type sourceType,
            Type destinationType,
            ResolutionContext context)
        {
            // Determine element types
            var srcElementType = GetCollectionElementType(sourceType);
            var destElementType = GetCollectionElementType(destinationType);
            if (srcElementType == null || destElementType == null)
            {
                // Not a collection we can handle; fall through to object mapping
                return MapObject(configuration, source, destination, sourceType, destinationType, null, context);
            }

            // Null source
            if (source == null)
            {
                var allowNull = AllowsNullCollections(configuration, null, sourceType, destinationType);
                return allowNull ? null : CreateEmptyCollection(destinationType);
            }

            var sourceList = source as IEnumerable ?? throw new MapItMappingException($"Source is not enumerable: {sourceType.Name}")
            { Types = new TypePair(sourceType, destinationType) };

            var budget = configuration.MaxCollectionItems;
            var count = 0;

            // Arrays are fixed-size; build a list then create the final array
            if (destinationType.IsArray)
            {
                var list = new List<object>();
                foreach (var item in sourceList)
                {
                    if (++count > budget) throw CollectionBudgetExceeded(budget, sourceType, destinationType);
                    list.Add(MapElement(configuration, item, destElementType, context));
                }
                var array = Array.CreateInstance(destElementType, list.Count);
                for (var i = 0; i < list.Count; i++)
                    array.SetValue(list[i], i);
                return array;
            }

            // Update-by-key into an existing destination collection.
            var elementMap = configuration.FindTypeMapFor(srcElementType, destElementType);
            if (destination is IList existingList && elementMap?.EqualityComparison != null)
            {
                return UpdateCollectionByKey(configuration, sourceList, existingList, srcElementType, destElementType, elementMap, context, budget);
            }

            var result = CreateEmptyCollection(destinationType);
            foreach (var item in sourceList)
            {
                if (++count > budget) throw CollectionBudgetExceeded(budget, sourceType, destinationType);
                AddToCollection(result, MapElement(configuration, item, destElementType, context));
            }

            return result;
        }

        /// <summary>
        /// Matches source items to existing destination items by the element map's EqualityComparison:
        /// updates matched items in place, adds new ones, removes destination items with no match.
        /// </summary>
        private static object UpdateCollectionByKey(
            IGlobalConfiguration configuration, IEnumerable sourceList, IList existing,
            Type srcElementType, Type destElementType, TypeMap elementMap, ResolutionContext context, int budget)
        {
            var existingItems = existing.Cast<object>().ToList();
            var matchedExisting = new HashSet<object>();
            var ordered = new List<object>();
            var count = 0;

            foreach (var srcItem in sourceList)
            {
                if (++count > budget) throw CollectionBudgetExceeded(budget, srcElementType, destElementType);
                object match = null;
                if (srcItem != null)
                {
                    foreach (var destItem in existingItems)
                    {
                        if (destItem == null || matchedExisting.Contains(destItem)) continue;
                        if (elementMap.EqualityComparison(srcItem, destItem)) { match = destItem; break; }
                    }
                }

                if (match != null)
                {
                    matchedExisting.Add(match);
                    var updated = Map(configuration, srcItem, match, srcItem.GetType(), destElementType, context,
                        new MapRequest(new TypePair(srcItem.GetType(), destElementType), new TypePair(srcItem.GetType(), destElementType), null));
                    ordered.Add(updated);
                }
                else
                {
                    ordered.Add(MapElement(configuration, srcItem, destElementType, context));
                }
            }

            existing.Clear();
            foreach (var item in ordered)
                existing.Add(item);
            return existing;
        }

        private static MapItMappingException CollectionBudgetExceeded(int budget, Type sourceType, Type destinationType) =>
            new MapItMappingException(
                $"Collection budget of {budget} exceeded mapping {sourceType?.Name} to {destinationType?.Name}. The source collection produced more items than allowed (MaxCollectionItems).")
            { Types = new TypePair(sourceType, destinationType) };

        /// <summary>Maps a single collection element to the destination element type.</summary>
        private static object MapElement(
            IGlobalConfiguration configuration,
            object item,
            Type destElementType,
            ResolutionContext context)
        {
            if (item == null)
                return null;
            if (item.GetType() == destElementType)
                return item;
            return Map(
                configuration,
                item,
                null,
                item.GetType(),
                destElementType,
                context,
                new MapRequest(
                    new TypePair(item.GetType(), destElementType),
                    new TypePair(item.GetType(), destElementType),
                    null));
        }

        /// <summary>Maps a source dictionary to a destination dictionary.</summary>
        private static object MapDictionary(
            IGlobalConfiguration configuration,
            object source,
            object destination,
            Type sourceType,
            Type destinationType,
            ResolutionContext context)
        {
            if (source == null)
                return AllowsNullCollections(configuration, null, sourceType, destinationType) ? null : CreateNew(destinationType);

            var srcDict = source as IDictionary;
            if (srcDict == null)
                return MapObject(configuration, source, destination, sourceType, destinationType, null, context);

            var budget = configuration.MaxCollectionItems;
            var count = 0;

            var destDictType = destinationType;
            var destDict = CreateNew(destDictType) as IDictionary
                ?? throw new MapItMappingException($"Destination is not a dictionary: {destinationType.Name}");

            // Get key/value element types
            Type keyType = null, valueType = null;
            foreach (var iface in destDictType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    keyType = iface.GetGenericArguments()[0];
                    valueType = iface.GetGenericArguments()[1];
                    break;
                }
            }

            foreach (DictionaryEntry entry in srcDict)
            {
                if (++count > budget) throw CollectionBudgetExceeded(budget, sourceType, destinationType);
                object mappedKey = entry.Key;
                object mappedValue = entry.Value;

                if (keyType != null && mappedKey != null && mappedKey.GetType() != keyType)
                    mappedKey = Map(configuration, mappedKey, null, mappedKey.GetType(), keyType, context,
                        new MapRequest(new TypePair(mappedKey.GetType(), keyType), new TypePair(mappedKey.GetType(), keyType), null));

                if (valueType != null && mappedValue != null && mappedValue.GetType() != valueType)
                    mappedValue = Map(configuration, mappedValue, null, mappedValue.GetType(), valueType, context,
                        new MapRequest(new TypePair(mappedValue.GetType(), valueType), new TypePair(mappedValue.GetType(), valueType), null));

                destDict.Add(mappedKey, mappedValue);
            }

            return destDict;
        }

        /// <summary>Whether a type is a dynamic/ExpandoObject target (IDictionary&lt;string,object&gt;).</summary>
        private static bool IsDynamic(Type type)
        {
            if (type == null) return false;
            if (type == typeof(System.Dynamic.ExpandoObject)) return true;
            if (typeof(System.Dynamic.IDynamicMetaObjectProvider).IsAssignableFrom(type)) return true;
            // IDictionary<string, object> is the ExpandoObject shape.
            return type.GetInterfaces().Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                && i.GetGenericArguments()[0] == typeof(string)
                && i.GetGenericArguments()[1] == typeof(object));
        }

        /// <summary>Maps a POCO source into a dynamic/ExpandoObject destination.</summary>
        private static object MapToDynamic(IGlobalConfiguration configuration, object source, Type sourceType, Type destinationType, ResolutionContext context)
        {
            IDictionary<string, object> dict;
            if (destinationType == typeof(System.Dynamic.ExpandoObject) || destinationType.IsInterface)
                dict = new System.Dynamic.ExpandoObject();
            else
                dict = (IDictionary<string, object>)CreateNew(destinationType);

            if (source == null) return dict;

            foreach (var prop in sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                dict[prop.Name] = ReflectionHelper.GetMemberValue(prop, source);
            }
            return dict;
        }

        /// <summary>Maps a dynamic/ExpandoObject source into a POCO destination.</summary>
        private static object MapFromDynamic(IGlobalConfiguration configuration, object source, object destination, Type destinationType, ResolutionContext context)
        {
            var dest = destination ?? CreateNew(destinationType);
            if (dest == null)
                throw new MapItMappingException($"Unable to create an instance of {destinationType.Name}.")
                { Types = new TypePair(source?.GetType(), destinationType) };

            var dict = source as IDictionary<string, object>;
            if (dict == null) return dest;

            foreach (var prop in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                // Case-insensitive key lookup.
                var key = dict.Keys.FirstOrDefault(k => string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));
                if (key == null) continue;
                var value = dict[key];
                if (value == null)
                {
                    if (!prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                        ReflectionHelper.SetMemberValue(prop, dest, null);
                    continue;
                }
                var srcType = value.GetType();
                var mapped = srcType == prop.PropertyType
                    ? value
                    : Map(configuration, value, null, srcType, prop.PropertyType, context,
                        new MapRequest(new TypePair(srcType, prop.PropertyType), new TypePair(srcType, prop.PropertyType), null));
                ReflectionHelper.SetMemberValue(prop, dest, mapped);
            }
            return dest;
        }

        /// <summary>Maps an enum to/from an enum, integral type, or string (BEHAVIOR §6).</summary>
        private static object MapEnum(object source, Type sourceType, Type destinationType, ResolutionContext context,
            Dictionary<object, object> enumValueOverrides = null)
        {
            var srcUnderlying = ReflectionHelper.GetUnderlyingNullableType(sourceType);
            var destUnderlying = ReflectionHelper.GetUnderlyingNullableType(destinationType);

            // Null source of a nullable-enum pair -> destination default.
            if (source == null)
                return CreateNew(destinationType);

            // Explicit value overrides (ConvertUsingEnumMapping) win.
            if (enumValueOverrides != null && srcUnderlying.IsEnum && destUnderlying.IsEnum)
            {
                var srcEnumVal = Enum.ToObject(srcUnderlying, source);
                if (enumValueOverrides.TryGetValue(srcEnumVal, out var mapped))
                    return mapped;
            }

            var ci = System.Globalization.CultureInfo.InvariantCulture;

            try
            {
                // string -> enum
                if (srcUnderlying == typeof(string) && destUnderlying.IsEnum)
                {
                    var s = (string)source;
                    return Enum.Parse(destUnderlying, s, ignoreCase: true);
                }
                // enum -> string
                if (srcUnderlying.IsEnum && destUnderlying == typeof(string))
                    return Enum.GetName(srcUnderlying, source) ?? Convert.ChangeType(source, Enum.GetUnderlyingType(srcUnderlying), ci).ToString();

                // enum -> enum
                if (srcUnderlying.IsEnum && destUnderlying.IsEnum)
                {
                    var name = Enum.GetName(srcUnderlying, source);
                    if (name != null)
                    {
                        var destNames = Enum.GetNames(destUnderlying);
                        var match = destNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                            return Enum.Parse(destUnderlying, match);
                    }
                    // numeric fallback via shared underlying
                    var numeric = Convert.ChangeType(source, Enum.GetUnderlyingType(srcUnderlying), ci);
                    return Enum.ToObject(destUnderlying, numeric);
                }

                // enum -> integral
                if (srcUnderlying.IsEnum && !destUnderlying.IsEnum)
                {
                    var numeric = Convert.ChangeType(source, Enum.GetUnderlyingType(srcUnderlying), ci);
                    if (destUnderlying == typeof(string)) return numeric.ToString();
                    return Convert.ChangeType(numeric, destUnderlying, ci);
                }

                // integral/string -> enum
                if (!srcUnderlying.IsEnum && destUnderlying.IsEnum)
                {
                    if (srcUnderlying == typeof(string))
                        return Enum.Parse(destUnderlying, (string)source, ignoreCase: true);
                    var numeric = Convert.ChangeType(source, Enum.GetUnderlyingType(destUnderlying), ci);
                    return Enum.ToObject(destUnderlying, numeric);
                }
            }
            catch (MapItMappingException) { throw; }
            catch (Exception)
            {
                throw new MapItMappingException($"Cannot convert enum value of type {sourceType.Name} to {destinationType.Name}.")
                { Types = new TypePair(sourceType, destinationType) };
            }

            throw new MapItMappingException($"Cannot convert enum value of type {sourceType.Name} to {destinationType.Name}.")
            { Types = new TypePair(sourceType, destinationType) };
        }

        private static readonly HashSet<Type> ConvertibleCommonTypes = new HashSet<Type>
        {
            typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
            typeof(Guid), typeof(bool), typeof(string)
        };

        /// <summary>Whether the source/destination types are primitive-convertible.</summary>
        private static bool IsPrimitiveConvertible(Type sourceType, Type destinationType)
        {
            var s = Unwrap(sourceType);
            var d = Unwrap(destinationType);
            if (s == d) return true;
            if (s.IsPrimitive || d.IsPrimitive) return true;
            if (d == typeof(string)) return true; // anything -> string via ToString/IFormattable
            if (s == typeof(string)) return true; // string -> many types via parse
            if (ConvertibleCommonTypes.Contains(s) || ConvertibleCommonTypes.Contains(d)) return true;
            if (FindConversionOperator(s, d) != null) return true;
            return false;
        }

        private static Type Unwrap(Type type) => ReflectionHelper.GetUnderlyingNullableType(type);

        /// <summary>Converts a primitive/common value to the destination type (BEHAVIOR §6).</summary>
        private static object ConvertPrimitive(object source, Type sourceType, Type destinationType)
        {
            if (source == null) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var destType = ReflectionHelper.GetUnderlyingNullableType(destinationType);
            var srcType = ReflectionHelper.GetUnderlyingNullableType(sourceType);

            // Already assignable.
            if (destType.IsInstanceOfType(source)) return source;

            // anything -> string.
            if (destType == typeof(string))
                return source is IFormattable f ? f.ToString(null, ci) : source.ToString();

            // User-defined conversion operators.
            var op = FindConversionOperator(srcType, destType);
            if (op != null)
                return op.Invoke(null, new[] { source });

            try
            {
                if (destType == typeof(Guid)) return Guid.Parse(source.ToString());
                if (destType == typeof(TimeSpan)) return source is TimeSpan ts ? ts : TimeSpan.Parse(source.ToString(), ci);
                if (destType == typeof(DateTimeOffset))
                    return source is DateTimeOffset dto ? dto : DateTimeOffset.Parse(source.ToString(), ci);
                if (destType == typeof(DateTime))
                    return source is DateTime dt ? dt : DateTime.Parse(source.ToString(), ci);
                if (destType == typeof(bool))
                {
                    if (source is bool b) return b;
                    if (source is string sb) return bool.Parse(sb);
                    return Convert.ToBoolean(source, ci);
                }
                if (destType.IsEnum)
                    return Enum.Parse(destType, source.ToString(), true);

                return Convert.ChangeType(source, destType, ci);
            }
            catch (InvalidCastException)
            {
                if (destType.IsEnum)
                    return Enum.Parse(destType, source.ToString(), true);
                throw new MapItMappingException($"Cannot convert value of type {sourceType.Name} to {destinationType.Name}.")
                { Types = new TypePair(sourceType, destinationType) };
            }
            catch (FormatException)
            {
                throw new MapItMappingException($"Cannot convert value of type {sourceType.Name} to {destinationType.Name}.")
                { Types = new TypePair(sourceType, destinationType) };
            }
        }

        private static MethodInfo FindConversionOperator(Type from, Type to)
        {
            MethodInfo Find(Type declaring)
            {
                return declaring.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => (m.Name == "op_Implicit" || m.Name == "op_Explicit")
                        && m.ReturnType == to
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == from);
            }
            return Find(to) ?? Find(from);
        }

        /// <summary>Gets the source type of a property map.</summary>
        private static Type sourceTypeOf(PropertyMap propMap) => propMap.SourceType;

        /// <summary>
        /// Matches a source member by name convention (case-insensitive exact match first, then prefix/postfix).
        /// </summary>
        private static MemberInfo[] MatchSourceMember(object source, Type sourceType, string destMemberName)
        {
            if (sourceType == null) return null;
            var properties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // 1. Exact case-insensitive match
            var exact = properties.FirstOrDefault(p => string.Equals(p.Name, destMemberName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return new[] { exact };

            // 2. Try with common prefixes stripped (e.g. "Name" -> source "UserName")
            return null;
        }

        /// <summary>Gets the value of a source member chain.</summary>
        private static object GetChainValue(object source, MemberInfo[] chain, out bool hasValue)
        {
            object current = source;
            foreach (var member in chain)
            {
                if (current == null)
                {
                    hasValue = false;
                    return null;
                }
                current = ReflectionHelper.GetMemberValue(member, current);
            }
            hasValue = true;
            return current;
        }

        /// <summary>Invokes a condition lambda (fallback path when only the expression is set).</summary>
        private static bool InvokeCondition(
            LambdaExpression condition,
            object source,
            object destination,
            object memberValue,
            ResolutionContext context)
        {
            try
            {
                var parameters = condition.Parameters;
                object[] args;
                if (parameters.Count == 0) args = new object[0];
                else if (parameters.Count == 1) args = new object[] { source };
                else if (parameters.Count == 2) args = new object[] { source, destination };
                else if (parameters.Count == 3) args = new object[] { source, destination, memberValue };
                else if (parameters.Count == 4) args = new object[] { source, destination, memberValue, null };
                else args = new object[] { source, destination, memberValue, null, context };
                var result = condition.Compile().DynamicInvoke(args);
                return (bool)(result ?? false);
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                // S7: do not swallow — surface the user's exception.
                throw new MapItMappingException("Error evaluating a mapping condition.", tie.InnerException ?? tie);
            }
        }

        /// <summary>Runs a list of actions (before/after map).</summary>
        private static void RunActions(List<MapAction> actions, object source, object dest, ResolutionContext context, Type sourceType, Type destinationType)
        {
            if (actions == null) return;
            foreach (var action in actions)
            {
                try { action.Action?.Invoke(source, dest, context); }
                catch (MapItMappingException) { throw; }
                catch (Exception ex)
                {
                    throw new MapItMappingException("Error in before/after map action.", ex)
                    { Types = new TypePair(sourceType, destinationType) };
                }
            }
        }

        /// <summary>Whether null destination values are allowed.</summary>
        private static bool AllowNullDestinationValues(IGlobalConfiguration configuration, Type sourceType, Type destinationType)
        {
            // A non-nullable value type can never receive null, whatever the profile says.
            if (destinationType.IsValueType && Nullable.GetUnderlyingType(destinationType) == null)
                return false;
            var profile = configuration.FindTypeMapFor(sourceType, destinationType)?.Profile;
            return profile?.AllowsNullDestinationValuesFor(null) ?? true;
        }

        /// <summary>
        /// Whether null collections are allowed (BEHAVIOR §5 step 8 / §10):
        /// member override, else declaring profile, else root cfg, else false.
        /// </summary>
        private static bool AllowsNullCollections(IGlobalConfiguration configuration, MemberMap memberMap, Type sourceType, Type destinationType)
        {
            if (memberMap != null && memberMap.AllowNull.HasValue)
                return memberMap.AllowNull.Value;

            // member level: use the member's owning type map profile.
            var ownerProfile = memberMap?.TypeMap?.Profile;
            if (ownerProfile != null)
                return ownerProfile.AllowsNullCollectionsFor(memberMap);

            // top level: use the element pair's map profile, else the root <global> profile.
            var srcElement = GetCollectionElementType(sourceType) ?? sourceType;
            var destElement = GetCollectionElementType(destinationType) ?? destinationType;
            var elementProfile = configuration.FindTypeMapFor(srcElement, destElement)?.Profile;
            if (elementProfile != null)
                return elementProfile.AllowNullCollections;

            if (configuration is MapperConfiguration mc && mc.RootProfileMap != null)
                return mc.RootProfileMap.AllowNullCollections;

            return false;
        }

        /// <summary>Creates a new instance of a type.</summary>
        private static object CreateNew(Type type)
        {
            if (type == null) return null;
            if (type.IsValueType) return Activator.CreateInstance(type);
            if (ReflectionHelper.IsNullableType(type)) return null;
            if (type.IsArray) return Activator.CreateInstance(type, 0);
            if (ReflectionHelper.IsCollection(type))
                return CreateEmptyCollection(type);
            if (type.IsDictionaryType())
                return Activator.CreateInstance(type);
            if (type == typeof(string)) return null;
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Creates an empty collection of a type.</summary>
        private static object CreateEmptyCollection(Type type)
        {
            if (type.IsArray) return Activator.CreateInstance(type, 0);
            if (type == typeof(string)) return string.Empty;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) return null;
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                // Fallback to List<T>
                var elementType = ReflectionHelper.GetEnumerableElementType(type);
                if (elementType != null)
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                throw new MapItMappingException($"Cannot create collection of type {type.Name}.");
            }
        }

        /// <summary>Adds an item to a collection.</summary>
        private static void AddToCollection(object collection, object item)
        {
            if (collection is IList list)
            {
                list.Add(item);
                return;
            }
            // Find an Add method
            var addMethod = collection.GetType().GetMethod("Add", new[] { item?.GetType() });
            if (addMethod != null)
            {
                addMethod.Invoke(collection, new[] { item });
                return;
            }
            throw new MapItMappingException($"Cannot add to collection of type {collection.GetType().Name}.");
        }

        /// <summary>Gets the element type of a collection type.</summary>
        private static Type GetCollectionElementType(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return type.GetElementType();
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Caches a reference for preserve-references.</summary>
        private static void CacheReference(TypeMap typeMap, object source, Type destinationType, object result, ResolutionContext context)
        {
            if (typeMap?.PreserveReferences != true) return;
            if (source == null || result == null) return;
            var key = new ContextCacheKey(source, destinationType);
            if (!context.InstanceCache.ContainsKey(key))
                context.InstanceCache[key] = result;
        }
    }
}
