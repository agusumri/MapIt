using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Expression for configuring the mapper.
    /// </summary>
    public interface IMapperConfigurationExpression : IProfileExpression
    {
        /// <summary>Adds a profile instance.</summary>
        void AddProfile(Profile profile);

        /// <summary>Adds a profile by type.</summary>
        void AddProfile(Type profileType);

        /// <summary>Adds a profile by generic type.</summary>
        void AddProfile<TProfile>() where TProfile : Profile, new();

        /// <summary>Adds multiple profiles.</summary>
        void AddProfiles(IEnumerable<Profile> enumerableOfProfiles);

        /// <summary>Creates a named profile.</summary>
        void CreateProfile(string profileName, Action<IProfileExpression> config);

        /// <summary>Scans assemblies for maps.</summary>
        void AddMaps(IEnumerable<Assembly> assembliesToScan);

        /// <summary>Scans assembly names for maps.</summary>
        void AddMaps(IEnumerable<string> assemblyNamesToScan);

        /// <summary>Scans types from assemblies for maps.</summary>
        void AddMaps(IEnumerable<Type> typesFromAssembliesContainingMappingDefinitions);

        /// <summary>Scans assemblies for maps.</summary>
        void AddMaps(Assembly[] assembliesToScan);

        /// <summary>Scans assembly names for maps.</summary>
        void AddMaps(string[] assemblyNamesToScan);

        /// <summary>Scans types from assemblies for maps.</summary>
        void AddMaps(Type[] typesFromAssembliesContainingMappingDefinitions);

        /// <summary>Sets the service constructor.</summary>
        void ConstructServicesUsing(Func<Type, object> constructor);

        /// <summary>Maximum nested mapping depth before the engine throws (BEHAVIOR S1). Default 256.</summary>
        int MaxGraphDepth { get; set; }

        /// <summary>Maximum items enumerated from one source collection (BEHAVIOR S5). Default 1,000,000.</summary>
        int MaxCollectionItems { get; set; }

        /// <summary>Merge duplicate type-pair maps instead of throwing at configuration time (BEHAVIOR S12). Default false.</summary>
        bool AllowAdditiveTypeMapCreation { get; set; }

        /// <summary>Registers a cross-cutting mapping interceptor.</summary>
        void AddInterceptor(IMappingInterceptor interceptor);
    }

    /// <summary>
    /// Global configuration (internal-facing but part of the public surface).
    /// </summary>
    public interface IGlobalConfiguration : IConfigurationProvider
    {
        /// <summary>Gets all configured type maps.</summary>
        IReadOnlyCollection<TypeMap> GetAllTypeMaps();

        /// <summary>Finds a type map for a type pair.</summary>
        TypeMap FindTypeMapFor(TypePair typePair);

        /// <summary>Finds a type map for source/destination types.</summary>
        TypeMap FindTypeMapFor(Type sourceType, Type destinationType);

        /// <summary>Finds a type map for typed types.</summary>
        TypeMap FindTypeMapFor<TSource, TDestination>();

        /// <summary>Gets a service constructor.</summary>
        Func<Type, object> ServiceCtor { get; }

        /// <summary>Gets the profiles.</summary>
        ProfileMap[] Profiles { get; }

        /// <summary>Gets the maximum execution plan depth.</summary>
        int MaxExecutionPlanDepth { get; }

        /// <summary>Gets the maximum nested mapping depth (BEHAVIOR S1), snapshotted at seal.</summary>
        int MaxGraphDepth { get; }

        /// <summary>Gets the maximum items enumerated from one source collection (BEHAVIOR S5), snapshotted at seal.</summary>
        int MaxCollectionItems { get; }

        /// <summary>Gets the registered object mappers.</summary>
        IEnumerable<IObjectMapper> GetMappers();

        /// <summary>Registers a type map.</summary>
        void RegisterTypeMap(TypeMap typeMap);

        /// <summary>Registers a map configuration.</summary>
        void RegisterAsMap(TypeMapConfiguration typeMapConfiguration);

        /// <summary>Validates a specific type map.</summary>
        void AssertConfigurationIsValid(TypeMap typeMap);

        /// <summary>Validates a specific profile.</summary>
        void AssertConfigurationIsValid(string profileName);

        /// <summary>Validates a specific profile by type.</summary>
        void AssertConfigurationIsValid<TProfile>();
    }

    /// <summary>
    /// Global configuration expression (internal-facing but part of the public surface).
    /// </summary>
    public interface IGlobalConfigurationExpression : IMapperConfigurationExpression
    {
        /// <summary>Adds a validator.</summary>
        void Validator(Action<ValidationContext> validator);

        /// <summary>Gets the profiles.</summary>
        IReadOnlyCollection<IProfileConfiguration> Profiles { get; }

        /// <summary>Gets the object mappers.</summary>
        List<IObjectMapper> Mappers { get; }

        /// <summary>Gets or sets the service constructor.</summary>
        Func<Type, object> ServiceCtor { get; set; }

        /// <summary>Gets the validators.</summary>
        List<Action<ValidationContext>> Validators { get; }

        /// <summary>Gets or sets the maximum execution plan depth.</summary>
        int MaxExecutionPlanDepth { get; set; }

        /// <summary>Gets or sets the recursive queries max depth.</summary>
        int RecursiveQueriesMaxDepth { get; set; }
    }

    /// <summary>
    /// Profile configuration (internal-facing but part of the public surface).
    /// </summary>
    public interface IProfileConfiguration
    {
        /// <summary>Gets or sets the profile name.</summary>
        string ProfileName { get; }

        /// <summary>Allows null collections.</summary>
        bool? AllowNullCollections { get; }

        /// <summary>Allows null destination values.</summary>
        bool? AllowNullDestinationValues { get; }

        /// <summary>Constructor mapping enabled.</summary>
        bool? ConstructorMappingEnabled { get; }

        /// <summary>Field mapping enabled.</summary>
        bool? FieldMappingEnabled { get; }

        /// <summary>Method mapping enabled.</summary>
        bool? MethodMappingEnabled { get; }

        /// <summary>Destination member naming convention.</summary>
        INamingConvention DestinationMemberNamingConvention { get; }

        /// <summary>Source member naming convention.</summary>
        INamingConvention SourceMemberNamingConvention { get; }

        /// <summary>Global ignores.</summary>
        IReadOnlyCollection<string> GlobalIgnores { get; }

        /// <summary>Type map configurations.</summary>
        IReadOnlyCollection<TypeMapConfiguration> TypeMapConfigs { get; }

        /// <summary>Open type map configurations.</summary>
        IReadOnlyCollection<TypeMapConfiguration> OpenTypeMapConfigs { get; }

        /// <summary>Determines whether a field should be mapped.</summary>
        Func<FieldInfo, bool> ShouldMapField { get; }

        /// <summary>Determines whether a method should be mapped.</summary>
        Func<MethodInfo, bool> ShouldMapMethod { get; }

        /// <summary>Determines whether a property should be mapped.</summary>
        Func<PropertyInfo, bool> ShouldMapProperty { get; }

        /// <summary>Determines whether a constructor should be used.</summary>
        Func<ConstructorInfo, bool> ShouldUseConstructor { get; }

        /// <summary>All type map actions.</summary>
        IReadOnlyCollection<Action<TypeMap, IMappingExpression>> AllTypeMapActions { get; }

        /// <summary>All property map actions.</summary>
        IReadOnlyCollection<Action<PropertyMap, IMemberConfigurationExpression>> AllPropertyMapActions { get; }

        /// <summary>Value transformer configurations.</summary>
        IReadOnlyCollection<ValueTransformerConfiguration> ValueTransformers { get; }
    }
}
