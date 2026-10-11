using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Configuration expression for the mapper. This is what you pass to MapperConfiguration.
    /// </summary>
    public class MapperConfigurationExpression : Profile, IMapperConfigurationExpression, IGlobalConfigurationExpression
    {
        /// <summary>
        /// Creates a new mapper configuration expression.
        /// </summary>
        public MapperConfigurationExpression()
            : base()
        {
        }

        /// <inheritdoc />
        public override string ProfileName => "<global>";

        #region IMapperConfigurationExpression

        /// <inheritdoc />
        public void AddProfile(Profile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _profiles.Add(profile);
        }

        /// <inheritdoc />
        public void AddProfile(Type profileType)
        {
            var profile = (Profile)Activator.CreateInstance(profileType);
            AddProfile(profile);
        }

        /// <inheritdoc />
        public void AddProfile<TProfile>() where TProfile : Profile, new() => AddProfile(new TProfile());

        /// <inheritdoc />
        public void AddProfiles(IEnumerable<Profile> enumerableOfProfiles)
        {
            foreach (var p in enumerableOfProfiles) AddProfile(p);
        }

        /// <inheritdoc />
        public void CreateProfile(string profileName, Action<IProfileExpression> config)
        {
            var profile = new Profile(profileName);
            config(profile);
            AddProfile(profile);
        }

        /// <inheritdoc />
        public void AddMaps(IEnumerable<Assembly> assembliesToScan)
        {
            foreach (var asm in assembliesToScan)
            {
                if (asm == null) continue;
                AddMaps(asm.GetExportedTypes());
            }
        }

        /// <inheritdoc />
        public void AddMaps(IEnumerable<string> assemblyNamesToScan)
        {
            foreach (var name in assemblyNamesToScan)
            {
                var asm = Assembly.Load(name);
                AddMaps((Assembly[])(new[] { asm }));
            }
        }

        /// <inheritdoc />
        public void AddMaps(IEnumerable<Type> typesFromAssembliesContainingMappingDefinitions)
        {
            foreach (var type in typesFromAssembliesContainingMappingDefinitions)
            {
                if (type == null) continue;

                // Profiles: concrete, non-generic, public parameterless ctor (S11).
                if (typeof(Profile).IsAssignableFrom(type))
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    AddProfile((Profile)Activator.CreateInstance(type));
                    continue;
                }

                // [AutoMap] decorated types.
                var autoMap = type.GetCustomAttribute<AutoMapAttribute>();
                if (autoMap != null)
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    ApplyAutoMap(type, autoMap);
                }
            }
        }

        private void ApplyAutoMap(Type decoratedType, AutoMapAttribute attr)
        {
            if (attr.SourceType == null) return;
            var exp = (MappingExpression)CreateMap(attr.SourceType, decoratedType);
            var typeMap = exp.TypeMap;

            if (attr.MaxDepth > 0) exp.MaxDepth(attr.MaxDepth);
            if (attr.PreserveReferences) exp.PreserveReferences();
            if (attr.DisableCtorValidation) exp.DisableCtorValidation();
            if (attr.IncludeAllDerived) ((IMappingExpressionBase<object, object, IMappingExpression>)exp).IncludeAllDerived();

            if (attr.ConstructUsingServiceLocator && typeMap != null)
                typeMap.ConstructViaServiceLocator = true;

            if (attr.TypeConverter != null)
                InstallAutoMapConverter(typeMap, attr.SourceType, decoratedType, attr.TypeConverter, attr.ConstructUsingServiceLocator);

            if (attr.ReverseMap)
                CreateMap(decoratedType, attr.SourceType);
        }

        private static void InstallAutoMapConverter(TypeMap typeMap, Type source, Type dest, Type converterType, bool viaServiceLocator)
        {
            var closed = converterType.IsGenericTypeDefinition ? SafeMakeGeneric(converterType, source, dest) : converterType;
            var iface = typeof(ITypeConverter<,>).MakeGenericType(source, dest);
            if (closed == null || !iface.IsAssignableFrom(closed))
                throw new MapItConfigurationException(
                    $"[AutoMap] TypeConverter {converterType.Name} on {dest.Name} does not implement ITypeConverter<{source.Name}, {dest.Name}>.")
                { Types = new TypePair(source, dest) };

            var convertMethod = iface.GetMethod("Convert");
            typeMap.TypeConverter = new TypeConverter
            {
                Convert = (s, d, ctx) =>
                {
                    var instance = ctx.ServiceCtor != null ? ctx.ServiceCtor(closed) : Activator.CreateInstance(closed);
                    return convertMethod.Invoke(instance, new[] { s, d, ctx });
                }
            };
        }

        private static Type SafeMakeGeneric(Type def, Type source, Type dest)
        {
            try { return def.MakeGenericType(source, dest); }
            catch { return null; }
        }

        /// <inheritdoc />
        public void AddMaps(Assembly[] assembliesToScan) => AddMaps((IEnumerable<Assembly>)assembliesToScan);

        /// <inheritdoc />
        public void AddMaps(string[] assemblyNamesToScan) => AddMaps((IEnumerable<string>)assemblyNamesToScan);

        /// <inheritdoc />
        public void AddMaps(Type[] typesFromAssembliesContainingMappingDefinitions) => AddMaps((IEnumerable<Type>)typesFromAssembliesContainingMappingDefinitions);

        /// <inheritdoc />
        public void ConstructServicesUsing(Func<Type, object> constructor) => ServiceCtor = constructor;

        #endregion

        #region IGlobalConfigurationExpression

        /// <inheritdoc />
        public void Validator(Action<ValidationContext> validator) => Validators.Add(validator);

        /// <inheritdoc />
        public IReadOnlyCollection<IProfileConfiguration> Profiles =>
            (IReadOnlyCollection<IProfileConfiguration>)_profiles.OfType<IProfileConfiguration>().ToList();

        /// <inheritdoc />
        public List<IObjectMapper> Mappers { get; } = new List<IObjectMapper>();

        /// <inheritdoc />
        public Func<Type, object> ServiceCtor { get; set; } = t => Activator.CreateInstance(t);

        /// <inheritdoc />
        public List<Action<ValidationContext>> Validators { get; } = new List<Action<ValidationContext>>();

        /// <inheritdoc />
        public int MaxExecutionPlanDepth { get; set; } = 10;

        /// <inheritdoc />
        public int RecursiveQueriesMaxDepth { get; set; } = 10;

        #endregion

        #region IMapperConfigurationExpression (safety options)

        /// <inheritdoc />
        public int MaxGraphDepth { get; set; } = MappingDefaults.MaxGraphDepth;

        /// <inheritdoc />
        public int MaxCollectionItems { get; set; } = MappingDefaults.MaxCollectionItems;

        /// <inheritdoc />
        public bool AllowAdditiveTypeMapCreation { get; set; } = false;

        #endregion

        #region MapIt extensions

        /// <summary>Global mapping interceptors invoked around every top-level map.</summary>
        internal List<IMappingInterceptor> Interceptors { get; } = new List<IMappingInterceptor>();

        /// <summary>Registers a cross-cutting mapping interceptor.</summary>
        public void AddInterceptor(IMappingInterceptor interceptor)
        {
            if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));
            Interceptors.Add(interceptor);
        }

        #endregion

        internal readonly List<Profile> _profiles = new List<Profile>();
    }
}
