using System;
using System.Linq;

namespace MapIt
{
    /// <summary>
    /// A pair of source and destination types.
    /// </summary>
    public struct TypePair : IEquatable<TypePair>
    {
        /// <summary>Creates a type pair.</summary>
        public TypePair(Type sourceType, Type destinationType)
        {
            SourceType = sourceType;
            DestinationType = destinationType;
        }

        /// <summary>Gets or sets the source type.</summary>
        public Type SourceType { get; set; }

        /// <summary>Gets or sets the destination type.</summary>
        public Type DestinationType { get; set; }

        /// <summary>Whether the pair contains generic parameters (open generics).</summary>
        public bool ContainsGenericParameters =>
            (SourceType?.ContainsGenericParameters ?? false) || (DestinationType?.ContainsGenericParameters ?? false);

        /// <summary>Whether the pair is a constructed generic type.</summary>
        public bool IsConstructedGenericType =>
            (SourceType?.IsConstructedGenericType ?? false) || (DestinationType?.IsConstructedGenericType ?? false);

        /// <summary>Closes generic types using a closed pair.</summary>
        public TypePair CloseGenericTypes(TypePair closedTypes)
        {
            var src = SourceType;
            var dest = DestinationType;
            if (src != null && src.IsGenericTypeDefinition && closedTypes.SourceType?.IsConstructedGenericType == true)
                src = src.MakeGenericType(closedTypes.SourceType.GetGenericArguments());
            if (dest != null && dest.IsGenericTypeDefinition && closedTypes.DestinationType?.IsConstructedGenericType == true)
                dest = dest.MakeGenericType(closedTypes.DestinationType.GetGenericArguments());
            return new TypePair(src, dest);
        }

        /// <summary>Gets the type definitions if generic.</summary>
        public TypePair GetTypeDefinitionIfGeneric()
        {
            Type src = SourceType;
            if (src != null && src.IsGenericType)
                src = GetGenericTypeDefinition(src);
            Type dest = DestinationType;
            if (dest != null && dest.IsGenericType)
                dest = GetGenericTypeDefinition(dest);
            return new TypePair(src, dest);
        }

        private static Type GetGenericTypeDefinition(Type type)
        {
            // Use the generic type definition via the type's generic parameter count
            if (!type.IsGenericType) return type;
            var args = type.GetGenericArguments();
            var name = type.Name;
            var idx = name.IndexOf('`');
            if (idx < 0) return type;
            var baseName = name.Substring(0, idx);
            // Find the open generic definition in the same assembly/namespace
            var candidates = type.Assembly.GetTypes();
            var match = candidates.FirstOrDefault(t =>
                t.IsGenericTypeDefinition &&
                t.GetGenericArguments().Length == args.Length &&
                (t.Name.IndexOf('`') < 0 || t.Name.Substring(0, t.Name.IndexOf('`')) == baseName));
            return match ?? type;
        }

        public bool Equals(TypePair other) =>
            Equals(SourceType, other.SourceType) && Equals(DestinationType, other.DestinationType);

        public override bool Equals(object obj) => obj is TypePair other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (SourceType?.GetHashCode() ?? 0);
                hash = hash * 31 + (DestinationType?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public void Deconstruct(out Type SourceType, out Type DestinationType)
        {
            SourceType = this.SourceType;
            DestinationType = this.DestinationType;
        }

        public override string ToString() => $"{SourceType?.Name ?? "<?>"} -> {DestinationType?.Name ?? "<?>"}";
    }
}
