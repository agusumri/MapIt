using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// The mapper. Created from a MapperConfiguration.
    /// </summary>
    public class Mapper : IRuntimeMapper, IMapper
    {
        /// <summary>
        /// Creates a mapper from a configuration.
        /// </summary>
        public Mapper(MapperConfiguration configuration) : this(configuration, null)
        {
        }

        /// <summary>
        /// Creates a mapper from a configuration with a per-mapper service constructor.
        /// </summary>
        internal Mapper(MapperConfiguration configuration, Func<Type, object> serviceCtor)
        {
            Configuration = configuration;
            ConfigurationProvider = configuration;
            _config = (IGlobalConfiguration)configuration;
            _serviceCtor = serviceCtor;
            _boundMapperFactory = ctx => new ContextBoundRuntimeMapper(this, ctx);
        }

        private readonly Func<ResolutionContext, IRuntimeMapper> _boundMapperFactory;

        /// <summary>Gets the configuration.</summary>
        public MapperConfiguration Configuration { get; }

        /// <inheritdoc />
        public IConfigurationProvider ConfigurationProvider { get; }

        private readonly IGlobalConfiguration _config;
        private readonly Func<Type, object> _serviceCtor;

        /// <summary>Per-mapper service constructor (null when none supplied).</summary>
        internal Func<Type, object> ServiceCtor => _serviceCtor;

        /// <inheritdoc />
        public TDestination Map<TDestination>(object source)
            => (TDestination)MapCore(source, null, typeof(TDestination), typeof(TDestination), null, null);

        /// <inheritdoc />
        public TDestination Map<TSource, TDestination>(TSource source)
            => (TDestination)MapCore(source, null, typeof(TSource), typeof(TDestination), null, null);

        /// <inheritdoc />
        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
            => (TDestination)MapCore(source, destination, typeof(TSource), typeof(TDestination), null, null);

        /// <inheritdoc />
        public object Map(object source, Type sourceType, Type destinationType)
            => MapCore(source, null, sourceType, destinationType, null, null);

        /// <inheritdoc />
        public object Map(object source, object destination, Type sourceType, Type destinationType)
            => MapCore(source, destination, sourceType, destinationType, null, null);

        /// <inheritdoc />
        public TDestination Map<TDestination>(object source, Action<IMappingOperationOptions<object, TDestination>> opts)
        {
            var options = new MappingOperationOptions<object, TDestination>();
            opts?.Invoke(options);
            return (TDestination)MapCore(source, null, source?.GetType() ?? typeof(object), typeof(TDestination), options, null);
        }

        /// <inheritdoc />
        public TDestination Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>> opts)
        {
            var options = new MappingOperationOptions<TSource, TDestination>();
            opts?.Invoke(options);
            return (TDestination)MapCore(source, null, typeof(TSource), typeof(TDestination), options, null);
        }

        /// <inheritdoc />
        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts)
        {
            var options = new MappingOperationOptions<TSource, TDestination>();
            opts?.Invoke(options);
            return (TDestination)MapCore(source, destination, typeof(TSource), typeof(TDestination), options, null);
        }

        /// <inheritdoc />
        public object Map(object source, Type sourceType, Type destinationType, Action<IMappingOperationOptions<object, object>> opts)
        {
            var options = new MappingOperationOptions<object, object>();
            opts?.Invoke(options);
            return MapCore(source, null, sourceType, destinationType, options, null);
        }

        /// <inheritdoc />
        public object Map(object source, object destination, Type sourceType, Type destinationType, Action<IMappingOperationOptions<object, object>> opts)
        {
            var options = new MappingOperationOptions<object, object>();
            opts?.Invoke(options);
            return MapCore(source, destination, sourceType, destinationType, options, null);
        }

        /// <inheritdoc />
        public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source, object parameters = null, params Expression<Func<TDestination, object>>[] membersToExpand)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var sourceType = source.ElementType;
            var paramDict = ObjectToDictionary(parameters);
            var expand = membersToExpand?.Select(ExtractMemberName).Where(n => n != null).ToList();
            var lambda = ProjectionBuilder.Build(Configuration, sourceType, typeof(TDestination), paramDict,
                expand != null && expand.Count > 0 ? new HashSet<string>(expand, StringComparer.Ordinal) : null);
            return (IQueryable<TDestination>)SelectQueryable(source, sourceType, typeof(TDestination), lambda);
        }

        /// <inheritdoc />
        public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source, IDictionary<string, object> parameters, params string[] membersToExpand)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var sourceType = source.ElementType;
            var lambda = ProjectionBuilder.Build(Configuration, sourceType, typeof(TDestination), parameters,
                membersToExpand != null && membersToExpand.Length > 0 ? new HashSet<string>(membersToExpand, StringComparer.Ordinal) : null);
            return (IQueryable<TDestination>)SelectQueryable(source, sourceType, typeof(TDestination), lambda);
        }

        /// <inheritdoc />
        public IQueryable ProjectTo(IQueryable source, Type destinationType, IDictionary<string, object> parameters = null, params string[] membersToExpand)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var sourceType = source.ElementType;
            var lambda = ProjectionBuilder.Build(Configuration, sourceType, destinationType, parameters,
                membersToExpand != null && membersToExpand.Length > 0 ? new HashSet<string>(membersToExpand, StringComparer.Ordinal) : null);
            return SelectQueryable(source, sourceType, destinationType, lambda);
        }

        private static IQueryable SelectQueryable(IQueryable source, Type sourceType, Type destinationType, LambdaExpression selector)
        {
            var select = typeof(Queryable).GetMethods()
                .First(m => m.Name == "Select" && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
                .MakeGenericMethod(sourceType, destinationType);
            return (IQueryable)select.Invoke(null, new object[] { source, selector });
        }

        private static IDictionary<string, object> ObjectToDictionary(object parameters)
        {
            if (parameters == null) return null;
            if (parameters is IDictionary<string, object> d) return d;
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parameters.GetType().GetProperties())
                dict[p.Name] = p.GetValue(parameters);
            return dict;
        }

        private static string ExtractMemberName<TDestination>(Expression<Func<TDestination, object>> expr)
        {
            var body = expr.Body;
            if (body is UnaryExpression ue) body = ue.Operand;
            return body is MemberExpression me ? me.Member.Name : null;
        }

        /// <summary>
        /// The core mapping method (top-level: creates a fresh context per operation).
        /// </summary>
        private object MapCore(
            object source,
            object destination,
            Type sourceType,
            Type destinationType,
            IMappingOperationOptions options,
            MemberMap memberMap)
        {
            var runtimeSourceType = source != null ? source.GetType() : sourceType;

            // Fastest path: a whole-graph compiled delegate. Only used when nothing per-call
            // (options, interceptors, an existing destination) could change the result.
            if (source != null && destination == null && options == null && memberMap == null && !Configuration.HasInterceptors)
            {
                var typeMap = _config.FindTypeMapFor(runtimeSourceType, destinationType);
                if (typeMap != null && typeMap.SourceType == runtimeSourceType && typeMap.DestinationType == destinationType)
                {
                    var plan = FastPlanCompiler.GetPlan(typeMap, _config);
                    if (plan != null && plan.CanRunPure(1))
                        return RunPure(plan, source, sourceType, runtimeSourceType, destinationType, typeMap);
                }
            }

            var mapRequest = new MapRequest(
                new TypePair(sourceType, destinationType),
                new TypePair(runtimeSourceType, destinationType),
                memberMap);

            // Nested context.Mapper.Map(...) must reuse this context; the facade is created lazily.
            var context = new ResolutionContext(_boundMapperFactory);

            // Per-operation service ctor wins; else per-mapper.
            context.ServiceCtor = (options as IMappingOperationOptionsInternal)?.ServiceCtorOverride ?? _serviceCtor;

            // Copy operation items to context.
            if (options != null)
            {
                foreach (var kv in options.Items)
                    context.Items[kv.Key] = kv.Value;
            }

            // Global interceptors wrap the top-level operation.
            if (Configuration.HasInterceptors)
            {
                var ictx = new MappingInterceptionContext(source, sourceType, destinationType, context);
                foreach (var interceptor in Configuration.Interceptors)
                {
                    var shortCircuit = interceptor.OnBeforeMap(ictx);
                    if (shortCircuit != null) return shortCircuit;
                }
                var result = MapWithContext(source, destination, runtimeSourceType, destinationType, context, mapRequest, options);
                for (var i = Configuration.Interceptors.Count - 1; i >= 0; i--)
                    result = Configuration.Interceptors[i].OnAfterMap(ictx, result);
                return result;
            }

            return MapWithContext(source, destination, runtimeSourceType, destinationType, context, mapRequest, options);
        }

        /// <summary>Creates a per-operation context for the compiled path (only when a fallback needs one).</summary>
        internal ResolutionContext CreateFastContext() =>
            new ResolutionContext(_boundMapperFactory) { ServiceCtor = _serviceCtor };

        /// <summary>Runs a whole-graph compiled plan with the same error contract as the engine (S7).</summary>
        private object RunPure(FastObjectPlan plan, object source, Type sourceType, Type runtimeSourceType, Type destinationType, TypeMap typeMap)
        {
            ResolutionContext context = null;
            try
            {
                return plan.Pure(source, this, ref context, 1);
            }
            catch (MapItMappingException mex)
            {
                if (mex.Types == null) mex.Types = new TypePair(sourceType, destinationType);
                if (mex.TypeMap == null) mex.TypeMap = typeMap;
                throw;
            }
            catch (Exception ex)
            {
                throw new MapItMappingException("Error mapping types.", ex)
                {
                    Types = new TypePair(sourceType, destinationType),
                    TypeMap = typeMap
                };
            }
        }

        /// <summary>
        /// Deep-clones a value by mapping its type onto itself, forcing construction of a new
        /// instance even for a convention-only self map.
        /// </summary>
        internal TValue CloneCore<TValue>(TValue source)
        {
            if (source == null) return default;
            var runtimeType = source.GetType();
            var mapRequest = new MapRequest(new TypePair(typeof(TValue), typeof(TValue)), new TypePair(runtimeType, runtimeType), null);
            var context = new ResolutionContext(this) { ForceConstruct = true };
            context.Mapper = new ContextBoundRuntimeMapper(this, context);
            context.ServiceCtor = _serviceCtor;
            return (TValue)MapWithContext(source, null, runtimeType, typeof(TValue), context, mapRequest);
        }

        /// <summary>
        /// Diff/patch mapping: maps <paramref name="source"/> onto <paramref name="destination"/>,
        /// writing only members whose mapped value differs, and reporting the changes.
        /// </summary>
        internal MapChangeResult<TDestination> MapChangesCore<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            var runtimeSourceType = source != null ? source.GetType() : typeof(TSource);
            var mapRequest = new MapRequest(
                new TypePair(typeof(TSource), typeof(TDestination)),
                new TypePair(runtimeSourceType, typeof(TDestination)),
                null);
            var changes = new List<MemberChange>();
            var context = new ResolutionContext(this) { ChangeTracker = changes };
            context.Mapper = new ContextBoundRuntimeMapper(this, context);
            context.ServiceCtor = _serviceCtor;
            var result = (TDestination)MapWithContext(source, destination, runtimeSourceType, typeof(TDestination), context, mapRequest);
            return new MapChangeResult<TDestination>(result, changes);
        }

        /// <summary>
        /// Asynchronous mapping: runs the synchronous pipeline, then awaits and applies any
        /// members configured with <c>MapFromAsync</c>.
        /// </summary>
        internal async System.Threading.Tasks.Task<TDestination> MapAsyncCore<TSource, TDestination>(
            TSource source, TDestination destination, System.Threading.CancellationToken cancellationToken)
        {
            var runtimeSourceType = source != null ? source.GetType() : typeof(TSource);
            var mapRequest = new MapRequest(
                new TypePair(typeof(TSource), typeof(TDestination)),
                new TypePair(runtimeSourceType, typeof(TDestination)),
                null);
            var pending = new List<PendingAsyncMember>();
            var context = new ResolutionContext(this) { PendingAsync = pending };
            context.Mapper = new ContextBoundRuntimeMapper(this, context);
            context.ServiceCtor = _serviceCtor;

            var result = MapWithContext(source, destination, runtimeSourceType, typeof(TDestination), context, mapRequest);

            foreach (var p in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = ReflectionHelper.GetMemberValue(p.PropertyMap.DestinationMember, p.Destination);
                var value = await p.PropertyMap.AsyncResolver(p.Source, p.Destination, existing, context, cancellationToken).ConfigureAwait(false);
                if (p.PropertyMap.CanBeSet)
                    ReflectionHelper.SetMemberValue(p.PropertyMap.DestinationMember, p.Destination, value);
            }

            return (TDestination)result;
        }

        /// <summary>
        /// Maps reusing an existing context (nested resolver-initiated maps share depth + cache).
        /// </summary>
        internal object MapWithContext(
            object source,
            object destination,
            Type runtimeSourceType,
            Type destinationType,
            ResolutionContext context,
            MapRequest mapRequest,
            IMappingOperationOptions options = null)
        {
            try
            {
                // Operation-level before-map runs before the map-level actions.
                var opItems = options as IMappingOperationOptionsInternal;
                opItems?.RunBeforeMap(source, destination, context);

                var result = MappingEngine.Map(
                    _config,
                    source,
                    destination,
                    runtimeSourceType,
                    destinationType,
                    context,
                    mapRequest);

                opItems?.RunAfterMap(source, result, context);

                return result;
            }
            catch (MapItMappingException mex)
            {
                // Fill gaps without overwriting (S7).
                if (mex.Types == null) mex.Types = mapRequest.RequestedTypes;
                if (mex.TypeMap == null) mex.TypeMap = _config.FindTypeMapFor(runtimeSourceType, destinationType);
                if (mex.MemberMap == null) mex.MemberMap = mapRequest.MemberMap;
                throw;
            }
            catch (Exception ex)
            {
                throw new MapItMappingException("Error mapping types.", ex)
                {
                    Types = mapRequest.RequestedTypes,
                    TypeMap = _config.FindTypeMapFor(runtimeSourceType, destinationType),
                    MemberMap = mapRequest.MemberMap
                };
            }
        }

        /// <summary>
        /// A runtime-mapper facade bound to a live context: nested Map calls reuse that context
        /// (shared instance cache and depth tracking) rather than starting a fresh operation.
        /// </summary>
        private sealed class ContextBoundRuntimeMapper : IRuntimeMapper
        {
            private readonly Mapper _owner;
            private readonly ResolutionContext _context;

            public ContextBoundRuntimeMapper(Mapper owner, ResolutionContext context)
            {
                _owner = owner;
                _context = context;
            }

            private object MapNested(object source, object destination, Type sourceType, Type destinationType)
            {
                var runtimeSourceType = source != null ? source.GetType() : sourceType;
                var mapRequest = new MapRequest(
                    new TypePair(sourceType, destinationType),
                    new TypePair(runtimeSourceType, destinationType),
                    null);
                return _owner.MapWithContext(source, destination, runtimeSourceType, destinationType, _context, mapRequest);
            }

            public TDestination Map<TDestination>(object source)
                => (TDestination)MapNested(source, null, source?.GetType() ?? typeof(TDestination), typeof(TDestination));

            public TDestination Map<TSource, TDestination>(TSource source)
                => (TDestination)MapNested(source, null, typeof(TSource), typeof(TDestination));

            public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
                => (TDestination)MapNested(source, destination, typeof(TSource), typeof(TDestination));

            public object Map(object source, Type sourceType, Type destinationType)
                => MapNested(source, null, sourceType, destinationType);

            public object Map(object source, object destination, Type sourceType, Type destinationType)
                => MapNested(source, destination, sourceType, destinationType);
        }
    }
}
