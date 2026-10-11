using System;

namespace MapIt
{
    /// <summary>
    /// A mapping request containing requested and runtime types.
    /// </summary>
    public struct MapRequest : IEquatable<MapRequest>
    {
        /// <summary>Creates a map request.</summary>
        public MapRequest(TypePair requestedTypes, TypePair runtimeTypes, MemberMap memberMap)
        {
            RequestedTypes = requestedTypes;
            RuntimeTypes = runtimeTypes;
            MemberMap = memberMap;
        }

        /// <summary>The requested (static/generic) types.</summary>
        public TypePair RequestedTypes { get; set; }

        /// <summary>The runtime types.</summary>
        public TypePair RuntimeTypes { get; set; }

        /// <summary>The member map associated with this request, if any.</summary>
        public MemberMap MemberMap { get; set; }

        public void Deconstruct(out TypePair RequestedTypes, out TypePair RuntimeTypes, out MemberMap MemberMap)
        {
            RequestedTypes = this.RequestedTypes;
            RuntimeTypes = this.RuntimeTypes;
            MemberMap = this.MemberMap;
        }

        public bool Equals(MapRequest other) =>
            RequestedTypes.Equals(other.RequestedTypes) && RuntimeTypes.Equals(other.RuntimeTypes);

        public override bool Equals(object obj) => obj is MapRequest other && Equals(other);

        public override int GetHashCode() => RequestedTypes.GetHashCode();

        public override string ToString() => $"MapRequest({RequestedTypes}, {RuntimeTypes})";
    }
}
