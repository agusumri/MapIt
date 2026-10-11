using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// The main configuration class. Create an instance, configure maps, then create a mapper.
    /// Configuration is sealed once in the constructor and frozen into an immutable snapshot;
    /// all runtime lookups read the snapshot (BEHAVIOR S6).
    /// </summary>
    public class MapperConfiguration : IGlobalConfiguration
    {
        internal MapperConfiguration(MapperConfigurationExpression configurationExpression)
        {
            _config = configurationExpression;
            Seal();
        }

        /// <summary>
        /// Creates a MapperConfiguration from an action.
        /// </summary>
        public MapperConfiguration(Action<IMapperConfigurationExpression> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            _config = new MapperConfigurationExpression();
            configure(_config);
            Seal();
        }

        /// <summary>
        /// Creates a MapperConfiguration from a pre-built configuration expression.
        /// </summary>
        public MapperConfiguration(IMapperConfigurationExpression configurationExpression)
        {
            _config = (MapperConfigurationExpression)configurationExpression ?? throw new ArgumentNullException(nameof(configurationExpression));
            Seal();
        }

        internal readonly MapperConfigurationExpression _config;

        // ---- Sealed snapshot (frozen in the constructor) ----
        private Dictionary<TypePair, TypeMap> _exactMaps;
        private IReadOnlyList<TypeMap> _openMaps;
        private readonly ConcurrentDictionary<TypePair, TypeMap> _closedMapCache = new ConcurrentDictionary<TypePair, TypeMap>();
        private List<TypeMap> _allMaps;
        private ProfileMap _rootProfileMap;
        private ProfileMap[] _profilesSnapshot;
        private int _maxGraphDepth;
        private int _maxCollectionItems;
        private bool _sealed;

        private void Seal()
        {
            if (_sealed) return;

            _maxGraphDepth = _config.MaxGraphDepth;
            _maxCollectionItems = _config.MaxCollectionItems;

            // (a) collect every TypeMap from the global expression and all profiles.
            _allMaps = new List<TypeMap>();
            _rootProfileMap = new ProfileMap(_config, _config);
            var profileMaps = new List<ProfileMap> { _rootProfileMap };

            void CollectFrom(Profile profile, ProfileMap profileMap)
            {
                foreach (var tm in profile.TypeMaps)
                {
                    tm.Profile = profileMap;
                    _allMaps.Add(tm);
                }
            }

            CollectFrom(_config, _rootProfileMap);
            foreach (var p in _config._profiles)
            {
                var pm = new ProfileMap(p, _config);
                profileMaps.Add(pm);
                CollectFrom(p, pm);
            }
            _profilesSnapshot = profileMaps.ToArray();

            // (c) convention discovery (run before S2 self-reference walk and attribute application).
            foreach (var tm in _allMaps)
            {
                if (tm.IsOpenGeneric) continue;      // closed on demand
                if (tm.TypeConverter != null) continue;
                DiscoverConventionMembers(tm);
            }

            // (c2) unflattening: reverse maps rebuild nested destination paths from flattened chains.
            foreach (var tm in _allMaps)
            {
                if (tm.IsOpenGeneric || tm.ReverseOf == null) continue;
                ApplyUnflattening(tm);
            }

            // (d) attribute application hook (Phase 3).
            AttributeConfigurationApplier.Apply(_allMaps, _config);

            // (f) S2 self-reference safety defaults (after convention discovery).
            foreach (var tm in _allMaps)
            {
                if (tm.IsOpenGeneric) continue;
                ApplySelfReferenceDefault(tm);
            }

            // (e) open-generic bookkeeping + S12 duplicate detection + build the exact map index.
            BuildExactMapIndexAndDetectDuplicates();

            // (g) seal every TypeMap.
            foreach (var tm in _allMaps)
                tm.Seal();

            _sealed = true;
        }

        private void ApplyUnflattening(TypeMap reverseMap)
        {
            var forward = reverseMap.ReverseOf;
            if (forward == null) return;

            foreach (var fpm in forward.PropertyMaps)
            {
                if (fpm.Ignored) continue;
                if (fpm.SourceMembers == null || fpm.SourceMembers.Length < 2) continue; // only flattened chains unflatten
                if (fpm.Resolver != null) continue;
                if (fpm.DestinationMember == null) continue;

                // Forward: Dst.<fpm.DestinationMember> <- Src.<fpm.SourceMembers (chain)>
                // Reverse: Src.<chain path> <- Dst.<fpm.DestinationMember>
                // Only unflatten chains whose members are all properties/fields (navigable paths).
                if (fpm.SourceMembers.Any(m => !(m is System.Reflection.PropertyInfo || m is System.Reflection.FieldInfo)))
                    continue;

                // The destination path on the reverse map must resolve on the reverse destination type (= Src).
                var path = new MemberPath(fpm.SourceMembers);
                // Skip if the reverse map already has an explicit path for this.
                if (reverseMap.PathMaps.Any(p => p.MemberPath.Equals(path))) continue;

                var pathMap = reverseMap.FindOrCreatePathMapFor(null, path, reverseMap);
                var srcMember = forward.SourceType.GetProperty(fpm.DestinationName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                // Reverse source member is the forward destination member, resolved on the reverse source type (= Dst).
                var reverseSourceMember = reverseMap.SourceType.GetProperty(fpm.DestinationName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (reverseSourceMember != null)
                {
                    pathMap.SourceMembers = new System.Reflection.MemberInfo[] { reverseSourceMember };
                    pathMap.IsExplicitlyConfigured = true;
                }
            }
        }

        private void BuildExactMapIndexAndDetectDuplicates()
        {
            _exactMaps = new Dictionary<TypePair, TypeMap>();
            var openMaps = new List<TypeMap>();
            var duplicates = new List<TypeMapConfigErrors>();
            var seen = new HashSet<TypePair>();

            foreach (var tm in _allMaps)
            {
                if (tm.IsOpenGeneric)
                {
                    openMaps.Add(tm);
                    continue;
                }

                var pair = tm.Types;
                if (seen.Contains(pair))
                {
                    if (_config.AllowAdditiveTypeMapCreation)
                    {
                        // Later wins: merge by replacing the exact entry.
                        _exactMaps[pair] = tm;
                    }
                    else
                    {
                        duplicates.Add(new TypeMapConfigErrors { TypeMap = tm, CanConstruct = true, UnmappedPropertyNames = Array.Empty<string>() });
                    }
                }
                else
                {
                    seen.Add(pair);
                    _exactMaps[pair] = tm;
                }
            }

            _openMaps = openMaps;

            if (duplicates.Count > 0 && !_config.AllowAdditiveTypeMapCreation)
                throw new DuplicateTypeMapConfigurationException(duplicates);
        }

        private void ApplySelfReferenceDefault(TypeMap typeMap)
        {
            if (typeMap.HasExplicitMaxDepth) return;
            if (typeMap.SourceType == null || typeMap.DestinationType == null) return;
            if (IsSelfReferential(typeMap.DestinationType, new HashSet<Type>()))
            {
                typeMap.MaxDepth = 64;
                typeMap.PreserveReferences = true;
            }
        }

        private bool IsSelfReferential(Type rootType, HashSet<Type> visited)
        {
            return ReachesType(rootType, rootType, visited);
        }

        private bool ReachesType(Type target, Type current, HashSet<Type> visited)
        {
            if (current == null) return false;
            if (!visited.Add(current)) return false;

            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                var memberType = prop.PropertyType;
                var elementType = ReflectionHelper.GetEnumerableElementType(memberType);
                var effective = elementType ?? ReflectionHelper.GetUnderlyingNullableType(memberType);
                if (effective == null) continue;
                if (effective == target) return true;
                if (IsComplexType(effective) && ReachesType(target, effective, visited))
                    return true;
            }
            return false;
        }

        private static bool IsComplexType(Type type)
        {
            if (type == null) return false;
            if (type.IsPrimitive || type.IsEnum) return false;
            if (type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid)) return false;
            if (ReflectionHelper.IsNullableType(type)) return false;
            return type.IsClass || (type.IsValueType && !type.IsPrimitive);
        }

        private void DiscoverConventionMembers(TypeMap typeMap)
        {
            var sourceType = typeMap.SourceType;
            var destinationType = typeMap.DestinationType;
            if (sourceType == null || destinationType == null) return;

            var profile = typeMap.Profile?.Profile;

            foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                if (typeMap.PropertyMaps.Any(p => p.DestinationMember == destProp &&
                        (p.SourceMembers != null || p.Resolver != null || p.AsyncResolver != null || p.Ignored || p.IsExplicitlyConfigured)))
                    continue;

                var srcChain = FindSourceChain(sourceType, destProp.Name, profile);

                // IncludeMembers: if not matched directly, look inside each included source member.
                if (srcChain == null && typeMap.IncludedMemberChains.Count > 0)
                {
                    foreach (var included in typeMap.IncludedMemberChains)
                    {
                        var includedType = ReflectionHelper.GetMemberType(included[included.Length - 1]);
                        if (includedType == null) continue;
                        var inner = FindSourceChain(includedType, destProp.Name, profile);
                        if (inner != null)
                        {
                            srcChain = included.Concat(inner).ToArray();
                            break;
                        }
                    }
                }

                var existing = typeMap.PropertyMaps.FirstOrDefault(p => p.DestinationMember == destProp);
                if (existing == null)
                    existing = typeMap.FindOrCreatePropertyMapFor(destProp, destProp.PropertyType);

                if (srcChain != null)
                    existing.SourceMembers = srcChain;
                else
                    existing.Ignored = true;   // convention-discovered, not explicitly configured
            }
        }

        private static MemberInfo[] FindSourceChain(Type sourceType, string destMemberName, Profile profile)
        {
            // Convention matching: direct, Get-method, naming conventions, prefix/postfix, flattening.
            var chain = MemberNameMatcher.Match(sourceType, destMemberName, profile);
            if (chain != null) return chain;

            // Extension-method source members (IncludeSourceExtensionMethods).
            if (profile != null)
            {
                foreach (var extType in profile.SourceExtensionMethodTypes)
                {
                    var m = extType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(mi => mi.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
                            && !mi.IsGenericMethodDefinition
                            && mi.ReturnType != typeof(void)
                            && mi.GetParameters().Length == 1
                            && mi.GetParameters()[0].ParameterType.IsAssignableFrom(sourceType)
                            && string.Equals(mi.Name, destMemberName, StringComparison.OrdinalIgnoreCase));
                    if (m != null) return new MemberInfo[] { m };
                }
            }
            return null;
        }

        /// <inheritdoc />
        public IMapper CreateMapper() => new Mapper(this);

        /// <inheritdoc />
        public IMapper CreateMapper(Func<Type, object> serviceCtor) => new Mapper(this, serviceCtor);

        /// <inheritdoc />
        public void AssertConfigurationIsValid()
        {
            var errors = ConfigurationValidator.Validate(GetValidatableMaps(), this, _config);
            if (errors.Count > 0)
                throw new MapItConfigurationException(errors);
        }

        /// <inheritdoc />
        public void AssertConfigurationIsValid(TypeMap typeMap)
        {
            if (typeMap == null) throw new ArgumentNullException(nameof(typeMap));
            var errors = ConfigurationValidator.Validate(new[] { typeMap }, this, _config);
            if (errors.Count > 0)
                throw new MapItConfigurationException(errors);
        }

        /// <inheritdoc />
        public void AssertConfigurationIsValid(string profileName)
        {
            var maps = GetValidatableMaps().Where(tm => string.Equals(tm.Profile?.Name, profileName, StringComparison.Ordinal)).ToList();
            var errors = ConfigurationValidator.Validate(maps, this, _config);
            if (errors.Count > 0)
                throw new MapItConfigurationException(errors);
        }

        /// <inheritdoc />
        public void AssertConfigurationIsValid<TProfile>()
        {
            var profileType = typeof(TProfile);
            var match = _config._profiles.FirstOrDefault(p => profileType.IsInstanceOfType(p));
            if (match == null)
                throw new ArgumentException($"Profile of type {profileType.Name} is not configured.");
            var maps = GetValidatableMaps().Where(tm => ReferenceEquals(tm.Profile?.Profile, match)).ToList();
            var errors = ConfigurationValidator.Validate(maps, this, _config);
            if (errors.Count > 0)
                throw new MapItConfigurationException(errors);
        }

        private IEnumerable<TypeMap> GetValidatableMaps() => _allMaps.Where(tm => !tm.IsOpenGeneric);

        /// <inheritdoc />
        public void CompileMappings()
        {
            // Precompilation is a no-op for the reflection engine.
        }

        /// <inheritdoc />
        public LambdaExpression BuildExecutionPlan(Type sourceType, Type destinationType)
            => ExecutionPlanShim.BuildExecutionPlan(this, sourceType, destinationType);

        /// <inheritdoc />
        public IReadOnlyCollection<TypeMap> GetAllTypeMaps() => _allMaps.AsReadOnly();

        /// <inheritdoc />
        public TypeMap FindTypeMapFor(TypePair typePair)
        {
            // 1. Exact frozen map, then the closed/resolved cache.
            if (_exactMaps.TryGetValue(typePair, out var exact))
                return exact;
            if (_closedMapCache.TryGetValue(typePair, out var cached))
                return cached;

            // 2. Polymorphism / included-derived dispatch.
            var polymorphic = ResolvePolymorphic(typePair);
            if (polymorphic != null)
                return _closedMapCache.GetOrAdd(typePair, polymorphic);

            // 3. Base-class then interface fallback.
            var fallback = ResolveBaseInterfaceFallback(typePair);
            if (fallback != null)
                return _closedMapCache.GetOrAdd(typePair, fallback);

            // 4. Open-generic close.
            var closed = ResolveOpenGeneric(typePair);
            if (closed != null)
                return _closedMapCache.GetOrAdd(typePair, closed);

            // 5/6. No map: built-in conversions are handled by the engine; otherwise null -> MissingMap.
            return null;
        }

        private TypeMap ResolvePolymorphic(TypePair typePair)
        {
            TypeMap best = null;
            foreach (var tm in _allMaps)
            {
                if (tm.IsOpenGeneric) continue;
                if (tm.SourceType == null || tm.DestinationType == null) continue;
                bool sourceAssignable = tm.SourceType.IsAssignableFrom(typePair.SourceType);
                bool destAssignable = tm.DestinationType.IsAssignableFrom(typePair.DestinationType);
                if (!sourceAssignable || !destAssignable) continue;
                if (tm.SourceType == typePair.SourceType && tm.DestinationType == typePair.DestinationType) continue;

                bool participates = tm.IncludeAllDerived
                    || tm.IncludedDerivedTypes.Any(p => p.SourceType.IsAssignableFrom(typePair.SourceType) && p.DestinationType.IsAssignableFrom(typePair.DestinationType));
                if (!participates) continue;

                if (best == null || tm.DestinationType.IsAssignableFrom(best.DestinationType))
                    best = tm;
            }
            return best;
        }

        private TypeMap ResolveBaseInterfaceFallback(TypePair typePair)
        {
            foreach (var baseType in ReflectionHelper.BaseClassesAndInterfaces(typePair.SourceType))
            {
                if (baseType == typePair.SourceType) continue;
                var candidate = new TypePair(baseType, typePair.DestinationType);
                if (_exactMaps.TryGetValue(candidate, out var tm))
                    return tm;
            }
            return null;
        }

        private TypeMap ResolveOpenGeneric(TypePair typePair)
        {
            if (_openMaps == null || _openMaps.Count == 0) return null;
            return OpenGenericCloser.TryClose(this, _openMaps, typePair);
        }

        /// <inheritdoc />
        public TypeMap FindTypeMapFor(Type sourceType, Type destinationType) =>
            FindTypeMapFor(new TypePair(sourceType, destinationType));

        /// <inheritdoc />
        public TypeMap FindTypeMapFor<TSource, TDestination>() =>
            FindTypeMapFor(typeof(TSource), typeof(TDestination));

        /// <inheritdoc />
        public Func<Type, object> ServiceCtor => _config.ServiceCtor;

        /// <inheritdoc />
        public ProfileMap[] Profiles => _profilesSnapshot;

        /// <summary>The sealed root &lt;global&gt; profile map (for top-level no-element-map lookups).</summary>
        internal ProfileMap RootProfileMap => _rootProfileMap;

        /// <summary>Frozen global mapping interceptors.</summary>
        internal IReadOnlyList<IMappingInterceptor> Interceptors => _config.Interceptors;

        /// <summary>Whether any interceptor is registered (fast path check).</summary>
        internal bool HasInterceptors => _config.Interceptors.Count > 0;

        /// <inheritdoc />
        public int MaxExecutionPlanDepth => _config.MaxExecutionPlanDepth;

        /// <inheritdoc />
        public int MaxGraphDepth => _maxGraphDepth;

        /// <inheritdoc />
        public int MaxCollectionItems => _maxCollectionItems;

        /// <summary>Recursive queries max depth (for ProjectTo nested-map truncation).</summary>
        internal int RecursiveQueriesMaxDepth => _config.RecursiveQueriesMaxDepth;

        /// <summary>Registers a closed map into the cache (used by the open-generic closer).</summary>
        internal TypeMap AddClosedMap(TypePair pair, TypeMap map) => _closedMapCache.GetOrAdd(pair, map);

        /// <summary>Runs convention discovery over a closed-on-demand map (used by the open-generic closer).</summary>
        internal void RunConventionDiscovery(TypeMap closedMap) => DiscoverConventionMembers(closedMap);

        /// <summary>Applies the S2 self-reference default to a closed-on-demand map.</summary>
        internal void ApplySelfReferenceToClosedMap(TypeMap closedMap) => ApplySelfReferenceDefault(closedMap);

        /// <inheritdoc />
        public IEnumerable<IObjectMapper> GetMappers() => _config.Mappers;

        /// <inheritdoc />
        public void RegisterTypeMap(TypeMap typeMap)
        {
            // Post-seal registration is a no-op on the frozen snapshot (BEHAVIOR S6).
        }

        /// <inheritdoc />
        public void RegisterAsMap(TypeMapConfiguration typeMapConfiguration)
        {
            // Open-generic registration occurs at configuration time via CreateMap(typeof(S<>), typeof(D<>)).
        }
    }
}
