using System;

namespace MapIt
{
    /// <summary>
    /// Convenience extensions: non-throwing mapping, deep cloning, and conditional mapping.
    /// All are built on the public <see cref="IMapper"/> surface and require no configuration changes.
    /// </summary>
    public static class MapperExtensions
    {
        /// <summary>
        /// Attempts to map <paramref name="source"/> to <typeparamref name="TDestination"/> without
        /// throwing. Returns false and <c>default</c> when mapping fails (e.g. missing map).
        /// </summary>
        public static bool TryMap<TDestination>(this IMapperBase mapper, object source, out TDestination result)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            try
            {
                result = mapper.Map<TDestination>(source);
                return true;
            }
            catch (MapItMappingException)
            {
                result = default;
                return false;
            }
        }

        /// <summary>
        /// Attempts to map <paramref name="source"/> to <typeparamref name="TDestination"/> without
        /// throwing. Returns false and <c>default</c> when mapping fails.
        /// </summary>
        public static bool TryMap<TSource, TDestination>(this IMapperBase mapper, TSource source, out TDestination result)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            try
            {
                result = mapper.Map<TSource, TDestination>(source);
                return true;
            }
            catch (MapItMappingException)
            {
                result = default;
                return false;
            }
        }

        /// <summary>
        /// Produces a deep clone of <paramref name="source"/> by mapping the type onto itself.
        /// Requires a configured <c>CreateMap&lt;T, T&gt;()</c>; nested same-type members are
        /// deep-copied rather than shared.
        /// </summary>
        public static TSource Clone<TSource>(this IMapper mapper, TSource source)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (source == null) return default;
            // Force a deep copy even for a convention-only self map.
            if (mapper is Mapper concrete)
                return concrete.CloneCore(source);
            return mapper.Map<TSource, TSource>(source);
        }

        /// <summary>
        /// Maps <paramref name="source"/> only when it is non-null; otherwise returns the supplied
        /// <paramref name="fallback"/> (default <c>default</c>). Avoids null-guard boilerplate.
        /// </summary>
        public static TDestination MapOrDefault<TDestination>(this IMapperBase mapper, object source, TDestination fallback = default)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            return source == null ? fallback : mapper.Map<TDestination>(source);
        }

        /// <summary>
        /// Diff/patch mapping: maps <paramref name="source"/>
        /// onto <paramref name="destination"/>, writing only members whose mapped value differs,
        /// and returning the patched destination together with the list of changed members.
        /// </summary>
        public static MapChangeResult<TDestination> MapChanges<TSource, TDestination>(this IMapper mapper, TSource source, TDestination destination)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (mapper is Mapper concrete)
                return concrete.MapChangesCore(source, destination);
            throw new NotSupportedException("MapChanges requires the MapIt Mapper implementation.");
        }

        /// <summary>
        /// Asynchronous mapping: maps synchronously, then awaits and applies any members
        /// configured with <c>MapFromAsync</c>.
        /// </summary>
        public static System.Threading.Tasks.Task<TDestination> MapAsync<TSource, TDestination>(
            this IMapper mapper, TSource source, System.Threading.CancellationToken cancellationToken = default)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (mapper is Mapper concrete)
                return concrete.MapAsyncCore<TSource, TDestination>(source, default, cancellationToken);
            throw new NotSupportedException("MapAsync requires the MapIt Mapper implementation.");
        }

        /// <summary>Asynchronous mapping onto an existing destination.</summary>
        public static System.Threading.Tasks.Task<TDestination> MapAsync<TSource, TDestination>(
            this IMapper mapper, TSource source, TDestination destination, System.Threading.CancellationToken cancellationToken = default)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (mapper is Mapper concrete)
                return concrete.MapAsyncCore(source, destination, cancellationToken);
            throw new NotSupportedException("MapAsync requires the MapIt Mapper implementation.");
        }
    }
}
