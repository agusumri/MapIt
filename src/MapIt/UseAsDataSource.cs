using System;
using System.Linq;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// Entry point for expression translation (<c>UseAsDataSource</c>): write queries against the
    /// destination/DTO type and have them translated and executed against the underlying
    /// source/entity <see cref="IQueryable"/>.
    /// </summary>
    public static class DataSourceExtensions
    {
        /// <summary>Begins a data-source projection over a source queryable.</summary>
        public static IDataSourceStage<TSource> UseAsDataSource<TSource>(this IQueryable<TSource> source, IMapper mapper)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            return new DataSourceStage<TSource>(source, (Mapper)mapper);
        }

        /// <summary>
        /// Translates a predicate written against <typeparamref name="TDestination"/> into one
        /// against <typeparamref name="TSource"/> using the configured map.
        /// </summary>
        public static Expression<Func<TSource, bool>> TranslateExpression<TDestination, TSource>(
            this IMapper mapper, Expression<Func<TDestination, bool>> destinationPredicate)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            var concrete = (Mapper)mapper;
            var lambda = ExpressionTranslator.Translate(concrete.Configuration, typeof(TDestination), typeof(TSource), destinationPredicate);
            return (Expression<Func<TSource, bool>>)lambda;
        }
    }

    /// <summary>A data-source projection stage: pick the destination type to query against.</summary>
    public interface IDataSourceStage<TSource>
    {
        /// <summary>Selects the destination type to write queries against.</summary>
        IDataSourceQuery<TSource, TDestination> For<TDestination>();
    }

    /// <summary>A data-source query over a source queryable, filtered via destination expressions.</summary>
    public interface IDataSourceQuery<TSource, TDestination>
    {
        /// <summary>Filters using a predicate expressed against the destination type.</summary>
        IDataSourceQuery<TSource, TDestination> Where(Expression<Func<TDestination, bool>> predicate);

        /// <summary>Projects the (filtered) source queryable to the destination type.</summary>
        IQueryable<TDestination> ToQueryable();
    }

    internal sealed class DataSourceStage<TSource> : IDataSourceStage<TSource>
    {
        private readonly IQueryable<TSource> _source;
        private readonly Mapper _mapper;
        public DataSourceStage(IQueryable<TSource> source, Mapper mapper) { _source = source; _mapper = mapper; }
        public IDataSourceQuery<TSource, TDestination> For<TDestination>() => new DataSourceQuery<TSource, TDestination>(_source, _mapper);
    }

    internal sealed class DataSourceQuery<TSource, TDestination> : IDataSourceQuery<TSource, TDestination>
    {
        private IQueryable<TSource> _source;
        private readonly Mapper _mapper;
        public DataSourceQuery(IQueryable<TSource> source, Mapper mapper) { _source = source; _mapper = mapper; }

        public IDataSourceQuery<TSource, TDestination> Where(Expression<Func<TDestination, bool>> predicate)
        {
            var translated = (Expression<Func<TSource, bool>>)ExpressionTranslator.Translate(
                _mapper.Configuration, typeof(TDestination), typeof(TSource), predicate);
            _source = _source.Where(translated);
            return this;
        }

        public IQueryable<TDestination> ToQueryable() => _mapper.ProjectTo<TDestination>(_source);
    }
}
