using System;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// A mapper that can handle a specific type pair (object mapper).
    /// </summary>
    public interface IObjectMapper
    {
        /// <summary>Whether this mapper matches the given type pair.</summary>
        bool IsMatch(TypePair context);

        /// <summary>Builds the mapping expression for the given type pair.</summary>
        Expression MapExpression(
            IGlobalConfiguration configuration,
            ProfileMap profileMap,
            MemberMap memberMap,
            Expression sourceExpression,
            Expression destExpression);
    }

    /// <summary>
    /// A validation context passed to custom validators.
    /// </summary>
    public struct ValidationContext : IEquatable<ValidationContext>
    {
        /// <summary>Creates a validation context.</summary>
        public ValidationContext(TypePair types, MemberMap memberMap, TypeMap typeMap, IObjectMapper objectMapper)
        {
            Types = types;
            MemberMap = memberMap;
            TypeMap = typeMap;
            ObjectMapper = objectMapper;
        }

        /// <summary>The types being validated.</summary>
        public TypePair Types { get; set; }

        /// <summary>The member map being validated.</summary>
        public MemberMap MemberMap { get; set; }

        /// <summary>The type map being validated.</summary>
        public TypeMap TypeMap { get; set; }

        /// <summary>The object mapper being validated.</summary>
        public IObjectMapper ObjectMapper { get; set; }

        public void Deconstruct(out TypePair Types, out MemberMap MemberMap, out TypeMap TypeMap, out IObjectMapper ObjectMapper)
        {
            Types = this.Types;
            MemberMap = this.MemberMap;
            TypeMap = this.TypeMap;
            ObjectMapper = this.ObjectMapper;
        }

        public bool Equals(ValidationContext other) => Types.Equals(other.Types);
        public override bool Equals(object obj) => obj is ValidationContext other && Equals(other);
        public override int GetHashCode() => Types.GetHashCode();
        public override string ToString() => "ValidationContext";
    }
}
