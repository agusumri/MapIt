using System;
using System.Linq;
using System.Text;

namespace MapIt
{
    /// <summary>
    /// A value transformer configuration for a value type.
    /// </summary>
    public struct ValueTransformerConfiguration : IEquatable<ValueTransformerConfiguration>
    {
        /// <summary>The value type the transformer applies to.</summary>
        public Type ValueType { get; set; }

        /// <summary>The transformer expression.</summary>
        public System.Linq.Expressions.LambdaExpression TransformerExpression { get; set; }

        /// <summary>A compiled transform delegate (set when built from a member AddTransform).</summary>
        internal Func<object, object> CompiledTransform { get; set; }

        /// <summary>Creates a value transformer configuration from a compiled delegate.</summary>
        internal ValueTransformerConfiguration(Type valueType, Func<object, object> compiled) : this()
        {
            ValueType = valueType;
            CompiledTransform = compiled;
        }

        /// <summary>Whether the transformer matches a member map.</summary>
        public bool IsMatch(MemberMap memberMap) =>
            memberMap != null && memberMap.DestinationType == ValueType;

        public bool Equals(ValueTransformerConfiguration other) =>
            Equals(ValueType, other.ValueType) && Equals(TransformerExpression, other.TransformerExpression);

        public override bool Equals(object obj) => obj is ValueTransformerConfiguration o && Equals(o);

        public override int GetHashCode() => ValueType?.GetHashCode() ?? 0;

        public void Deconstruct(out Type ValueType, out System.Linq.Expressions.LambdaExpression TransformerExpression)
        {
            ValueType = this.ValueType;
            TransformerExpression = this.TransformerExpression;
        }

        public override string ToString() => "ValueTransformerConfiguration";
    }

    /// <summary>
    /// Exact-match naming convention (default): names must match exactly.
    /// </summary>
    public class ExactMatchNamingConvention : INamingConvention
    {
        /// <summary>The separator character.</summary>
        public string SeparatorCharacter => null;

        /// <summary>Splits the input (exact match returns the whole word).</summary>
        public string[] Split(string input) => new[] { input };
    }

    /// <summary>
    /// PascalCase naming convention.
    /// </summary>
    public class PascalCaseNamingConvention : INamingConvention
    {
        /// <summary>The separator character.</summary>
        public string SeparatorCharacter => "";

        /// <summary>Splits a PascalCase name into words.</summary>
        public string[] Split(string input) => SplitCase(input, false);

        internal static string[] SplitCase(string input, bool lowerUnderscore)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<string>();
            var words = new System.Collections.Generic.List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                bool isUpper = char.IsUpper(c);
                bool prevUpper = i > 0 && char.IsUpper(input[i - 1]);
                bool nextUpper = i + 1 < input.Length && char.IsUpper(input[i + 1]);
                if (i > 0 && (isUpper && !prevUpper && !nextUpper) || (isUpper && prevUpper && nextUpper))
                {
                    if (sb.Length > 0) words.Add(Normalize(sb.ToString(), lowerUnderscore));
                    sb.Clear();
                }
                sb.Append(c);
            }
            if (sb.Length > 0) words.Add(Normalize(sb.ToString(), lowerUnderscore));
            return words.ToArray();
        }

        private static string Normalize(string word, bool lowerUnderscore) =>
            lowerUnderscore ? word.ToLowerInvariant() : word;
    }

    /// <summary>
    /// Lower-underscore naming convention (snake_case).
    /// </summary>
    public class LowerUnderscoreNamingConvention : INamingConvention
    {
        /// <summary>The separator character.</summary>
        public string SeparatorCharacter => "_";

        /// <summary>Splits a lower_underscore name into words.</summary>
        public string[] Split(string input) => string.IsNullOrEmpty(input) ? Array.Empty<string>() : input.Split('_');
    }
}
