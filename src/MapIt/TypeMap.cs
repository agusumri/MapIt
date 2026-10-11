using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A map between a source and destination type, including all its member maps.
    /// </summary>
    public class TypeMap
    {
        internal TypeMap(Type sourceType, Type destinationType, MemberList configuredMemberList)
        {
            SourceType = sourceType;
            DestinationType = destinationType;
            ConfiguredMemberList = configuredMemberList;
            PropertyMaps = new List<PropertyMap>();
            PathMaps = new List<PathMap>();
            SourceMemberConfigs = new Dictionary<MemberInfo, SourceMemberConfig>(MemberInfoComparer.Instance);
        }

        /// <summary>Gets the source type.</summary>
        public Type SourceType { get; }

        /// <summary>Gets the destination type.</summary>
        public Type DestinationType { get; internal set; }

        /// <summary>Gets or sets the configured member list.</summary>
        public MemberList ConfiguredMemberList { get; set; }

        /// <summary>Gets the property maps.</summary>
        public IReadOnlyCollection<PropertyMap> PropertyMaps { get; }

        /// <summary>Gets the path maps.</summary>
        public IReadOnlyCollection<PathMap> PathMaps { get; }

        /// <summary>Gets all member maps (property + path + ctor).</summary>
        public IEnumerable<MemberMap> MemberMaps =>
            PropertyMaps.OfType<MemberMap>().Concat(PathMaps.OfType<MemberMap>());

        /// <summary>Gets or sets the source member configurations.</summary>
        internal IReadOnlyDictionary<MemberInfo, SourceMemberConfig> SourceMemberConfigs { get; set; }

        /// <summary>Gets or sets the before-map actions.</summary>
        internal List<MapAction> BeforeMapActions { get; } = new List<MapAction>();

        /// <summary>Gets or sets the after-map actions.</summary>
        internal List<MapAction> AfterMapActions { get; } = new List<MapAction>();

        /// <summary>Gets or sets the type converter (if ConvertUsing used).</summary>
        internal TypeConverter TypeConverter { get; set; }

        /// <summary>Gets or sets the custom ctor expression.</summary>
        internal CustomCtor CustomCtor { get; set; }

        /// <summary>Gets or sets whether ctor validation is disabled.</summary>
        public bool DisableConstructorValidation { get; set; }

        /// <summary>Gets or sets the max depth.</summary>
        public int MaxDepth { get; set; } = int.MaxValue;

        /// <summary>True when the user explicitly configured MaxDepth, so the S2 self-reference default does not override it.</summary>
        internal bool HasExplicitMaxDepth { get; set; }

        /// <summary>Gets or sets whether references are preserved.</summary>
        public bool PreserveReferences { get; set; }

        /// <summary>When set, any configured map whose source/destination derive from this map's participates in polymorphic dispatch.</summary>
        internal bool IncludeAllDerived { get; set; }

        /// <summary>Open-generic destination-type override (As(Type) with an open definition).</summary>
        internal Type DestinationTypeOverride { get; set; }

        /// <summary>Gets or sets the constructor map.</summary>
        internal ConstructorMap ConstructorMap { get; set; }

        /// <summary>Explicit constructor parameter maps (ForCtorParam).</summary>
        internal List<ConstructorParameterMap> ConstructorParameterMaps { get; } = new List<ConstructorParameterMap>();

        /// <summary>Gets or sets the profile map.</summary>
        internal ProfileMap Profile { get; set; }

        /// <summary>True when this map is an open-generic definition (S&lt;&gt; or D&lt;&gt;).</summary>
        internal bool IsOpenGeneric =>
            (SourceType != null && SourceType.IsGenericTypeDefinition) ||
            (DestinationType != null && DestinationType.IsGenericTypeDefinition);

        /// <summary>The open-generic carrier recording configuration steps replayed at close time.</summary>
        internal OpenGenericTypeMap OpenGenericTypeMap { get; set; }

        /// <summary>Types pairs that this map includes (Include/IncludeBase), for polymorphic dispatch.</summary>
        internal List<TypePair> IncludedDerivedTypes { get; } = new List<TypePair>();

        /// <summary>Types this map inherits from (IncludeBase).</summary>
        internal List<TypePair> IncludedBaseTypes { get; } = new List<TypePair>();

        /// <summary>Source member chains whose members are flattened into this destination (IncludeMembers).</summary>
        internal List<MemberInfo[]> IncludedMemberChains { get; } = new List<MemberInfo[]>();

        /// <summary>
        /// Optional equality comparison (source, destination) used to match items when mapping into
        /// an existing destination collection ("update by key" behavior).
        /// </summary>
        internal Func<object, object, bool> EqualityComparison { get; set; }

        /// <summary>Explicit enum value overrides (source enum value -> destination enum value).</summary>
        internal Dictionary<object, object> EnumValueOverrides { get; set; }

        /// <summary>Flag indicating that this attribute member was declared [MapAtRuntime] (recorded, no runtime effect per §11).</summary>
        internal bool MapAtRuntime { get; set; }

        /// <summary>The forward map this map was created as the reverse of (for unflattening at seal).</summary>
        internal TypeMap ReverseOf { get; set; }

        /// <summary>Construct the destination via the service locator (ServiceCtor).</summary>
        internal bool ConstructViaServiceLocator { get; set; }

        private bool _sealed;

        /// <summary>Freezes the type map against further mutation.</summary>
        internal void Seal() => _sealed = true;

        /// <summary>Whether the type map has been sealed.</summary>
        internal bool IsSealed => _sealed;

        /// <summary>
        /// True iff this map carries user-meaningful configuration: a whole-map converter, a
        /// custom ctor, any explicit ctor-param map, or at least one explicitly-configured
        /// member map. Convention-discovered members do NOT count (BEHAVIOR §6).
        /// </summary>
        internal bool HasConfiguredMembersOrCtorOrConverter =>
            TypeConverter != null ||
            CustomCtor != null ||
            ConstructorParameterMaps.Count > 0 ||
            PropertyMaps.Concat<MemberMap>(PathMaps).Any(m => m.IsExplicitlyConfigured);

        /// <summary>Gets the type pair.</summary>
        public TypePair Types => new TypePair(SourceType, DestinationType);

        /// <summary>Finds or creates a property map for a destination property.</summary>
        public PropertyMap FindOrCreatePropertyMapFor(MemberInfo destinationProperty, Type destinationPropertyType)
        {
            var existing = PropertyMaps.FirstOrDefault(p => p.DestinationMember == destinationProperty);
            if (existing != null) return existing;
            if (_sealed) throw new InvalidOperationException("Configuration is sealed.");
            var map = new PropertyMap
            {
                DestinationMember = destinationProperty,
                DestinationName = destinationProperty.Name,
                DestinationType = destinationPropertyType,
                SourceType = SourceType,
                TypeMap = this,
                CanBeSet = ReflectionHelper.CanBeSet(destinationProperty)
            };
            ((List<PropertyMap>)PropertyMaps).Add(map);
            return map;
        }

        /// <summary>Finds or creates a path map for a destination expression.</summary>
        public PathMap FindOrCreatePathMapFor(LambdaExpression destinationExpression, MemberPath path, TypeMap typeMap)
        {
            var existing = PathMaps.FirstOrDefault(p => p.MemberPath.Equals(path));
            if (existing != null) return existing;
            if (_sealed) throw new InvalidOperationException("Configuration is sealed.");
            var last = path.Last;
            var map = new PathMap
            {
                DestinationName = path.ToString(),
                DestinationType = (last != null) ? ReflectionHelper.GetMemberType(last) : typeof(object),
                MemberPath = path,
                DestinationExpression = destinationExpression,
                SourceType = SourceType,
                TypeMap = this
            };
            ((List<PathMap>)PathMaps).Add(map);
            return map;
        }

        /// <summary>Finds or creates a source member config.</summary>
        public SourceMemberConfig FindOrCreateSourceMemberConfigFor(MemberInfo sourceMember)
        {
            var configs = (Dictionary<MemberInfo, SourceMemberConfig>)SourceMemberConfigs;
            if (!configs.TryGetValue(sourceMember, out var config))
            {
                config = new SourceMemberConfig { SourceMember = sourceMember };
                configs[sourceMember] = config;
            }
            return config;
        }

        /// <summary>Adds a before-map action.</summary>
        public void AddBeforeMapAction(MapAction action)
        {
            if (_sealed) throw new InvalidOperationException("Configuration is sealed.");
            BeforeMapActions.Add(action);
        }

        /// <summary>Adds an after-map action.</summary>
        public void AddAfterMapAction(MapAction action)
        {
            if (_sealed) throw new InvalidOperationException("Configuration is sealed.");
            AfterMapActions.Add(action);
        }

        /// <summary>Gets the names of destination properties that are not mapped.</summary>
        public string[] GetUnmappedPropertyNames()
        {
            var mapped = new HashSet<string>(PropertyMaps.Where(p => p.IsMapped && !p.Ignored).Select(p => p.DestinationMember.Name));
            var unmapped = new List<string>();
            foreach (var prop in DestinationType?.GetProperties(BindingFlags.Public | BindingFlags.Instance) ?? Array.Empty<PropertyInfo>())
            {
                if (prop.CanWrite && !mapped.Contains(prop.Name))
                    unmapped.Add(prop.Name);
            }
            return unmapped.ToArray();
        }

        /// <summary>Creates a missing-map exception.</summary>
        public static Exception MissingMapException(TypePair types) =>
            new MapItMappingException($"Missing type map configuration or unsupported mapping: {types}");

        /// <summary>Creates a missing-map exception.</summary>
        public static Exception MissingMapException(Type sourceType, Type destinationType) =>
            MissingMapException(new TypePair(sourceType, destinationType));

        public override string ToString() => Types.ToString();

        private class MemberInfoComparer : IEqualityComparer<MemberInfo>
        {
            public static readonly MemberInfoComparer Instance = new MemberInfoComparer();
            public bool Equals(MemberInfo x, MemberInfo y) =>
                x?.DeclaringType == y?.DeclaringType && x?.Name == y?.Name && x?.MemberType == y?.MemberType;
            public int GetHashCode(MemberInfo obj) =>
                (obj?.DeclaringType?.GetHashCode() ?? 0) * 31 + (obj?.Name?.GetHashCode() ?? 0);
        }
    }

    /// <summary>
    /// A type converter for a whole map.
    /// </summary>
    public class TypeConverter
    {
        /// <summary>The converter expression or function.</summary>
        public Func<object, object, ResolutionContext, object> Convert { get; set; }
    }

    /// <summary>
    /// A custom constructor for a map.
    /// </summary>
    public class CustomCtor
    {
        /// <summary>The ctor function.</summary>
        public Func<object, ResolutionContext, object> Ctor { get; set; }
    }

    /// <summary>
    /// A constructor map for a destination type.
    /// </summary>
    public class ConstructorMap
    {
        /// <summary>Gets or sets the constructor.</summary>
        public ConstructorInfo Ctor { get; set; }

        /// <summary>Gets or sets the ctor parameter maps.</summary>
        public IReadOnlyCollection<ConstructorParameterMap> CtorParams { get; set; } = Array.Empty<ConstructorParameterMap>();

        /// <summary>Gets or sets whether the ctor can be resolved.</summary>
        public bool CanResolve { get; set; } = true;
    }

    /// <summary>
    /// A before/after map action, carrying source/destination at execution time.
    /// </summary>
    public struct MapAction
    {
        /// <summary>The action to invoke with source, destination, and context.</summary>
        public Action<object, object, ResolutionContext> Action { get; set; }
    }
}
