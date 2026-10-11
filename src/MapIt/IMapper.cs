using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// Core mapping interface.
    /// </summary>
    public interface IMapperBase
    {
        /// <summary>Maps a source to a typed destination.</summary>
        TDestination Map<TDestination>(object source);

        /// <summary>Maps a typed source to a typed destination.</summary>
        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>Maps a source onto an existing destination.</summary>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);

        /// <summary>Maps between runtime types.</summary>
        object Map(object source, Type sourceType, Type destinationType);

        /// <summary>Maps between runtime types onto an existing destination.</summary>
        object Map(object source, object destination, Type sourceType, Type destinationType);
    }

    /// <summary>
    /// The full mapper interface.
    /// </summary>
    public interface IMapper : IMapperBase
    {
        /// <summary>Maps with operation options.</summary>
        TDestination Map<TDestination>(object source, Action<IMappingOperationOptions<object, TDestination>> opts);

        /// <summary>Maps a typed source with operation options.</summary>
        TDestination Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>> opts);

        /// <summary>Maps a source onto an existing destination with operation options.</summary>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts);

        /// <summary>Maps between runtime types with operation options.</summary>
        object Map(object source, Type sourceType, Type destinationType, Action<IMappingOperationOptions<object, object>> opts);

        /// <summary>Maps between runtime types onto an existing destination with operation options.</summary>
        object Map(object source, object destination, Type sourceType, Type destinationType, Action<IMappingOperationOptions<object, object>> opts);

        /// <summary>
        /// Projects a queryable to a destination queryable (queryable/EF projection).
        /// </summary>
        IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source, object parameters = null, params Expression<Func<TDestination, object>>[] membersToExpand);

        /// <summary>
        /// Projects a queryable to a destination queryable.
        /// </summary>
        IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source, IDictionary<string, object> parameters, params string[] membersToExpand);

        /// <summary>
        /// Projects a queryable to a destination queryable (runtime type).
        /// </summary>
        IQueryable ProjectTo(IQueryable source, Type destinationType, IDictionary<string, object> parameters = null, params string[] membersToExpand);

        /// <summary>Gets the configuration provider used to build this mapper.</summary>
        IConfigurationProvider ConfigurationProvider { get; }
    }

    /// <summary>
    /// Runtime mapper exposed to resolvers/converters for recursive mapping.
    /// </summary>
    public interface IRuntimeMapper : IMapperBase
    {
    }
}
