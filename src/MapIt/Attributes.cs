using System;

namespace MapIt
{
    /// <summary>
    /// Provides configuration for a member.
    /// </summary>
    public interface IMemberConfigurationProvider
    {
        /// <summary>Applies configuration to a member.</summary>
        void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression);
    }

    /// <summary>
    /// Marks a type as auto-mappable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoMapAttribute : Attribute
    {
        /// <summary>Creates an AutoMap attribute.</summary>
        public AutoMapAttribute() { }

        /// <summary>Creates an AutoMap attribute with a source type.</summary>
        public AutoMapAttribute(Type sourceType) => SourceType = sourceType;

        /// <summary>Creates an AutoMap attribute with source and destination types.</summary>
        public AutoMapAttribute(Type sourceType, Type destinationType)
        {
            SourceType = sourceType;
            DestinationType = destinationType;
        }

        /// <summary>Gets the source type.</summary>
        public Type SourceType { get; }

        /// <summary>Gets or sets the destination type.</summary>
        public Type DestinationType { get; set; }

        /// <summary>Whether the map is a reverse map.</summary>
        public bool ReverseMap { get; set; }

        /// <summary>Whether all derived types are included.</summary>
        public bool IncludeAllDerived { get; set; }

        /// <summary>The maximum depth.</summary>
        public int MaxDepth { get; set; }

        /// <summary>Whether references are preserved.</summary>
        public bool PreserveReferences { get; set; }

        /// <summary>Whether ctor validation is disabled.</summary>
        public bool DisableCtorValidation { get; set; }

        /// <summary>An optional whole-map type converter (must implement ITypeConverter&lt;Source, Destination&gt;).</summary>
        public Type TypeConverter { get; set; }

        /// <summary>Whether the destination is constructed via the service locator (ServiceCtor).</summary>
        public bool ConstructUsingServiceLocator { get; set; }

        /// <summary>Applies configuration to a mapping expression.</summary>
        public void ApplyConfiguration(IMappingExpression mappingExpression)
        {
            // Configuration is applied by the configuration scanner
        }
    }

    /// <summary>
    /// Marks a member to be ignored.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class IgnoreAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Marks a member to be mapped at runtime.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class MapAtRuntimeAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Sets the mapping order for a member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class MappingOrderAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Creates a MappingOrder attribute.</summary>
        public MappingOrderAttribute(int value) => Value = value;

        /// <summary>Gets the mapping order value.</summary>
        public int Value { get; }

        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Provides a null substitute for a member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class NullSubstituteAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Creates a NullSubstitute attribute.</summary>
        public NullSubstituteAttribute(object value) => Value = value;

        /// <summary>Gets the null substitute value.</summary>
        public object Value { get; }

        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Maps a member from a named source member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SourceMemberAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Creates a SourceMember attribute.</summary>
        public SourceMemberAttribute(string name) => Name = name;

        /// <summary>Gets the source member name.</summary>
        public string Name { get; }

        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Uses the destination's existing value for a member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class UseExistingValueAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Uses a value resolver for a member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ValueResolverAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Creates a ValueResolver attribute.</summary>
        public ValueResolverAttribute(Type type) => Type = type;

        /// <summary>Gets the value resolver type.</summary>
        public Type Type { get; }

        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }

    /// <summary>
    /// Uses a value converter for a member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ValueConverterAttribute : Attribute, IMemberConfigurationProvider
    {
        /// <summary>Creates a ValueConverter attribute.</summary>
        public ValueConverterAttribute(Type type) => Type = type;

        /// <summary>Gets the value converter type.</summary>
        public Type Type { get; }

        /// <summary>Applies configuration to a member.</summary>
        public void ApplyConfiguration(IMemberConfigurationExpression memberConfigurationExpression) { }
    }
}
