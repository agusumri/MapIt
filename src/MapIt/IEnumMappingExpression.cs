using System;
using System.Collections.Generic;

namespace MapIt
{
    /// <summary>
    /// Configures explicit enum-to-enum value overrides. Unmapped values fall back to the
    /// default by-name then by-numeric convention.
    /// </summary>
    public interface IEnumMappingExpression<TSource, TDestination>
    {
        /// <summary>Maps a specific source enum value to a specific destination enum value.</summary>
        IEnumMappingExpression<TSource, TDestination> MapValue(TSource source, TDestination destination);
    }

    internal sealed class EnumMappingExpression<TSource, TDestination> : IEnumMappingExpression<TSource, TDestination>
    {
        public Dictionary<object, object> Overrides { get; } = new Dictionary<object, object>();

        public IEnumMappingExpression<TSource, TDestination> MapValue(TSource source, TDestination destination)
        {
            Overrides[source] = destination;
            return this;
        }
    }
}
