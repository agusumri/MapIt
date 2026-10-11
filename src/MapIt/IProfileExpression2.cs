using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Expression for configuring a mapping profile.
    /// </summary>
    public interface IProfileExpression
    {
        /// <summary>Creates a map between types.</summary>
        IMappingExpression CreateMap(Type sourceType, Type destinationType);

        /// <summary>Creates a map between types with a member list.</summary>
        IMappingExpression CreateMap(Type sourceType, Type destinationType, MemberList memberList);

        /// <summary>Creates a map between typed types.</summary>
        IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>();

        /// <summary>Creates a map between typed types with a member list.</summary>
        IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(MemberList memberList);

        /// <summary>Creates a projection-only map.</summary>
        IProjectionExpression<TSource, TDestination> CreateProjection<TSource, TDestination>();

        /// <summary>Creates a projection-only map with a member list.</summary>
        IProjectionExpression<TSource, TDestination> CreateProjection<TSource, TDestination>(MemberList memberList);

        /// <summary>Adds a global ignore by property-name prefix.</summary>
        void AddGlobalIgnore(string propertyNameStartingWith);

        /// <summary>Clears recognized destination prefixes.</summary>
        void ClearPrefixes();

        /// <summary>Disables constructor parameter mapping.</summary>
        void DisableConstructorMapping();

        /// <summary>Includes extension methods from a type as source members.</summary>
        void IncludeSourceExtensionMethods(Type type);

        /// <summary>Recognizes destination postfixes for member-name matching.</summary>
        void RecognizeDestinationPostfixes(string[] postfixes);

        /// <summary>Recognizes destination prefixes for member-name matching.</summary>
        void RecognizeDestinationPrefixes(string[] prefixes);

        /// <summary>Recognizes postfixes for member-name matching.</summary>
        void RecognizePostfixes(string[] postfixes);

        /// <summary>Recognizes prefixes for member-name matching.</summary>
        void RecognizePrefixes(string[] prefixes);

        /// <summary>Replaces a destination member name when matching to source.</summary>
        void ReplaceMemberName(string original, string newValue);

        /// <summary>Allows null collections to map to null.</summary>
        bool? AllowNullCollections { get; set; }

        /// <summary>Allows null destination values.</summary>
        bool? AllowNullDestinationValues { get; set; }

        /// <summary>Destination member naming convention.</summary>
        INamingConvention DestinationMemberNamingConvention { get; set; }

        /// <summary>Source member naming convention.</summary>
        INamingConvention SourceMemberNamingConvention { get; set; }

        /// <summary>Gets the profile name.</summary>
        string ProfileName { get; }

        /// <summary>Determines whether a field should be mapped.</summary>
        Func<FieldInfo, bool> ShouldMapField { get; set; }

        /// <summary>Determines whether a method should be mapped.</summary>
        Func<MethodInfo, bool> ShouldMapMethod { get; set; }

        /// <summary>Determines whether a property should be mapped.</summary>
        Func<PropertyInfo, bool> ShouldMapProperty { get; set; }

        /// <summary>Determines whether a constructor should be used.</summary>
        Func<ConstructorInfo, bool> ShouldUseConstructor { get; set; }

        /// <summary>Gets the value transformer configurations for this profile.</summary>
        List<ValueTransformerConfiguration> ValueTransformers { get; }
    }
}
