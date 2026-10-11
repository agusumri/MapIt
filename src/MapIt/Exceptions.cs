using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MapIt
{
    /// <summary>
    /// Thrown when a mapping error occurs during execution.
    /// </summary>
    public class MapItMappingException : Exception
    {
        /// <summary>Creates a new mapping exception.</summary>
        public MapItMappingException() { }

        /// <summary>Creates a new mapping exception with a message.</summary>
        public MapItMappingException(string message) : base(message) { }

        /// <summary>Creates a new mapping exception wrapping an inner exception.</summary>
        public MapItMappingException(string message, Exception innerException) : base(message, Unwrap(innerException)) { }

        // Reflection/DynamicInvoke wrap user exceptions in TargetInvocationException; callers should
        // see their own exception as InnerException.
        private static Exception Unwrap(Exception ex)
        {
            while (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                ex = tie.InnerException;
            return ex;
        }

        /// <summary>Gets or sets the member map that failed, if any.</summary>
        public MemberMap MemberMap { get; set; }

        /// <summary>Gets or sets the type map involved in the error, if any.</summary>
        public TypeMap TypeMap { get; set; }

        /// <summary>Gets or sets the source/destination type pair involved in the error.</summary>
        public TypePair? Types { get; set; }
    }

    /// <summary>
    /// Thrown when the configuration is invalid.
    /// </summary>
    public class MapItConfigurationException : Exception
    {
        /// <summary>Creates a new configuration exception with a message.</summary>
        public MapItConfigurationException(string message) : base(message) { }

        /// <summary>Gets or sets the member map that failed, if any.</summary>
        public MemberMap MemberMap { get; set; }

        /// <summary>Gets or sets the type pair involved in the error, if any.</summary>
        public TypePair? Types { get; set; }

        /// <summary>Gets the list of configuration errors.</summary>
        public TypeMapConfigErrors[] Errors { get; }

        /// <summary>Creates a new configuration exception from a set of errors.</summary>
        public MapItConfigurationException(IEnumerable<TypeMapConfigErrors> errors) : base(BuildMessage(errors))
        {
            Errors = errors?.ToArray() ?? Array.Empty<TypeMapConfigErrors>();
        }

        private static string BuildMessage(IEnumerable<TypeMapConfigErrors> errors)
        {
            var list = errors?.ToList() ?? new List<TypeMapConfigErrors>();
            var sb = new StringBuilder();

            // Lead line is conditional: only say "Unmapped members" when there actually are any.
            var anyUnmapped = list.Any(e => e.UnmappedPropertyNames != null && e.UnmappedPropertyNames.Length > 0);
            sb.AppendLine(anyUnmapped
                ? "Unmapped members were found. Review the configurations and either map explicitly or ignore."
                : "Configuration validation failed. Review the configurations below.");

            foreach (var e in list)
            {
                sb.AppendLine($"  {e.TypeMap?.SourceType?.Name ?? "<?>"} -> {e.TypeMap?.DestinationType?.Name ?? "<?>"}");
                if (e.CanConstruct == false)
                    sb.AppendLine("    Cannot construct destination (no accessible constructor).");
                if (e.UnmappedPropertyNames != null)
                    foreach (var name in e.UnmappedPropertyNames)
                        sb.AppendLine($"    Unmapped: {name}");
                if (e.UnmappableNestedPairs != null)
                    foreach (var pair in e.UnmappableNestedPairs)
                        sb.AppendLine($"    No map or conversion for nested member pair: {pair}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// A single configuration validation error for a type map.
    /// </summary>
    public class TypeMapConfigErrors
    {
        internal TypeMapConfigErrors() { }

        /// <summary>Gets or sets the type map that failed.</summary>
        public TypeMap TypeMap { get; set; }

        /// <summary>Gets or sets whether the destination can be constructed.</summary>
        public bool CanConstruct { get; set; }

        /// <summary>Gets or sets the names of destination members that could not be mapped.</summary>
        public string[] UnmappedPropertyNames { get; set; }

        /// <summary>Gets or sets nested member type-pairs that have no map or built-in conversion.</summary>
        public string[] UnmappableNestedPairs { get; set; }
    }

    /// <summary>
    /// Thrown when a type map is configured more than once.
    /// </summary>
    public class DuplicateTypeMapConfigurationException : Exception
    {
        /// <summary>Creates a new duplicate-type-map exception with a message.</summary>
        public DuplicateTypeMapConfigurationException(string message) : base(message) { }

        /// <summary>Gets the list of duplicate map errors.</summary>
        public TypeMapConfigErrors[] Errors { get; }

        /// <summary>Creates a new duplicate-type-map exception from a set of errors.</summary>
        public DuplicateTypeMapConfigurationException(IEnumerable<TypeMapConfigErrors> errors) : base(BuildMessage(errors))
        {
            Errors = errors?.ToArray() ?? Array.Empty<TypeMapConfigErrors>();
        }

        private static string BuildMessage(IEnumerable<TypeMapConfigErrors> errors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Configuration contains multiple type maps with the same source/destination type pair:");
            foreach (var e in errors)
                sb.AppendLine($"  {e.TypeMap?.SourceType?.Name ?? "<?>"} -> {e.TypeMap?.DestinationType?.Name ?? "<?>"}");
            return sb.ToString();
        }
    }
}
