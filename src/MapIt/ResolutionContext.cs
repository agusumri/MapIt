using System;
using System.Collections.Generic;

namespace MapIt
{
    /// <summary>
    /// Provides context for a mapping operation, including the mapper, per-operation items,
    /// and the instance cache used for reference-preservation (cycles).
    /// </summary>
    public class ResolutionContext
    {
        /// <summary>
        /// Gets the mapper that is performing the current mapping. Nested Map calls through this
        /// mapper reuse the current context (same instance cache + depth tracking, BEHAVIOR §7).
        /// </summary>
        public IRuntimeMapper Mapper
        {
            get
            {
                if (_mapper == null && _mapperFactory != null)
                {
                    _mapper = _mapperFactory(this);
                    _mapperFactory = null;
                }
                return _mapper;
            }
            internal set
            {
                _mapper = value;
                _mapperFactory = null;
            }
        }

        private IRuntimeMapper _mapper;
        private Func<ResolutionContext, IRuntimeMapper> _mapperFactory;
        private Dictionary<string, object> _items;
        private Dictionary<ContextCacheKey, object> _instanceCache;
        private Dictionary<TypeMap, int> _typeMapOccurrences;

        /// <summary>
        /// Gets a dictionary of arbitrary user items for the current mapping operation.
        /// Created on first access.
        /// </summary>
        public Dictionary<string, object> Items =>
            _items ?? (_items = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Gets the instance cache used to preserve references (avoid infinite recursion).
        /// Created on first access.
        /// </summary>
        public Dictionary<ContextCacheKey, object> InstanceCache =>
            _instanceCache ?? (_instanceCache = new Dictionary<ContextCacheKey, object>());

        /// <summary>
        /// Creates a new resolution context.
        /// </summary>
        public ResolutionContext(IRuntimeMapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        /// <summary>
        /// Creates a context whose <see cref="Mapper"/> is built on first access (avoids an
        /// allocation per mapping call when no resolver needs it).
        /// </summary>
        internal ResolutionContext(Func<ResolutionContext, IRuntimeMapper> mapperFactory)
        {
            _mapperFactory = mapperFactory ?? throw new ArgumentNullException(nameof(mapperFactory));
        }

        /// <summary>
        /// Current nested mapping depth (BEHAVIOR S1). Incremented on entry to the engine's
        /// <c>Map</c> and decremented on exit. Never shared across threads (one context per
        /// top-level operation).
        /// </summary>
        internal int CurrentDepth { get; set; }

        /// <summary>
        /// Per-TypeMap occurrence depth along the current recursion path, used for S3 MaxDepth
        /// truncation.
        /// </summary>
        internal Dictionary<TypeMap, int> TypeMapOccurrences =>
            _typeMapOccurrences ?? (_typeMapOccurrences = new Dictionary<TypeMap, int>());

        /// <summary>
        /// Per-operation service constructor override (from opts.ConstructServicesUsing). When
        /// set, it wins over the per-mapper and sealed-config service constructors.
        /// </summary>
        internal Func<Type, object> ServiceCtor { get; set; }

        /// <summary>
        /// When true, a configured same-type (T-&gt;T) map constructs a new destination instead of
        /// passing the source through (enables MapIt's deep <c>Clone</c>).
        /// </summary>
        internal bool ForceConstruct { get; set; }

        /// <summary>
        /// When set, the engine writes a destination member only if the mapped value differs from
        /// its current value, and records each change here (MapIt diff/patch mapping).
        /// </summary>
        internal List<MemberChange> ChangeTracker { get; set; }

        /// <summary>
        /// When set (async mapping), members carrying an async resolver are deferred here instead
        /// of being resolved synchronously; MapAsync awaits and applies them after the sync pass.
        /// </summary>
        internal List<PendingAsyncMember> PendingAsync { get; set; }

        /// <summary>Increments the occurrence count for a type map and returns the new count.</summary>
        internal int EnterTypeMap(TypeMap typeMap)
        {
            if (typeMap == null) return 0;
            TypeMapOccurrences.TryGetValue(typeMap, out var count);
            count++;
            TypeMapOccurrences[typeMap] = count;
            return count;
        }

        /// <summary>Decrements the occurrence count for a type map.</summary>
        internal void ExitTypeMap(TypeMap typeMap)
        {
            if (typeMap == null) return;
            if (TypeMapOccurrences.TryGetValue(typeMap, out var count))
            {
                count--;
                if (count <= 0) TypeMapOccurrences.Remove(typeMap);
                else TypeMapOccurrences[typeMap] = count;
            }
        }

        /// <summary>
        /// Attempts to retrieve the per-operation items.
        /// </summary>
        public bool TryGetItems(out Dictionary<string, object> items)
        {
            items = Items;
            return items != null;
        }
    }

    /// <summary>
    /// Key for the instance cache: a source object combined with its destination type.
    /// </summary>
    public struct ContextCacheKey : IEquatable<ContextCacheKey>
    {
        /// <summary>Creates a cache key.</summary>
        public ContextCacheKey(object source, Type destinationType)
        {
            Source = source;
            DestinationType = destinationType;
        }

        /// <summary>The source object.</summary>
        public object Source { get; set; }

        /// <summary>The destination type.</summary>
        public Type DestinationType { get; set; }

        public bool Equals(ContextCacheKey other) =>
            ReferenceEquals(Source, other.Source) && Equals(DestinationType, other.DestinationType);

        public override bool Equals(object obj) => obj is ContextCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                // BEHAVIOR S4: the instance cache keys on source *reference identity* and must
                // never call user Equals/GetHashCode.
                hash = hash * 31 + (Source == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Source));
                hash = hash * 31 + (DestinationType?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public void Deconstruct(out object Source, out Type DestinationType)
        {
            Source = this.Source;
            DestinationType = this.DestinationType;
        }

        public override string ToString() => $"(Source: {Source?.GetType().Name ?? "null"}, DestinationType: {DestinationType?.Name ?? "null"})";
    }

    /// <summary>A member whose async resolver is deferred for MapAsync to await and apply.</summary>
    internal sealed class PendingAsyncMember
    {
        public PendingAsyncMember(object source, object destination, PropertyMap propertyMap)
        {
            Source = source;
            Destination = destination;
            PropertyMap = propertyMap;
        }

        public object Source { get; }
        public object Destination { get; }
        public PropertyMap PropertyMap { get; }
    }
}
