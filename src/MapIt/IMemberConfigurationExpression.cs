using System;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Configuration options for a destination member.
    /// </summary>
    public interface IMemberConfigurationExpression
    {
        /// <summary>Maps from a value resolver type.</summary>
        void MapFrom(Type valueResolverType);

        /// <summary>Maps from a value resolver type with a source member.</summary>
        void MapFrom(Type valueResolverType, string sourceMemberName);

        /// <summary>Maps using a member value resolver.</summary>
        void MapFrom<TSource, TDestination, TSourceMember, TDestMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TDestMember> resolver, string sourceMemberName);

        /// <summary>Uses a value converter type.</summary>
        void ConvertUsing(Type valueConverterType);

        /// <summary>Uses a value converter type with a source member.</summary>
        void ConvertUsing(Type valueConverterType, string sourceMemberName);

        /// <summary>Uses a value converter with a source member.</summary>
        void ConvertUsing<TSourceMember, TDestinationMember>(IValueConverter<TSourceMember, TDestinationMember> valueConverter, string sourceMemberName);
    }

    /// <summary>
    /// Configuration options for a destination member with typed source/destination.
    /// </summary>
    public interface IMemberConfigurationExpression<TSource, TDestination, TMember> :
        IMemberConfigurationExpression
    {
        /// <summary>Maps from a source member path (string).</summary>
        void MapFrom(string sourceMembersPath);

        /// <summary>Maps from a source member expression.</summary>
        void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> mapExpression);

        /// <summary>Maps using a mapping function.</summary>
        void MapFrom<TResult>(Func<TSource, TDestination, TResult> mappingFunction);

        /// <summary>Maps using a mapping function with source member.</summary>
        void MapFrom<TResult>(Func<TSource, TDestination, TMember, TResult> mappingFunction);

        /// <summary>Maps using a mapping function with context.</summary>
        void MapFrom<TResult>(Func<TSource, TDestination, TMember, ResolutionContext, TResult> mappingFunction);

        /// <summary>Maps using a value resolver.</summary>
        void MapFrom(IValueResolver<TSource, TDestination, TMember> valueResolver);

        /// <summary>Maps using a member value resolver.</summary>
        void MapFrom<TSourceMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TMember> valueResolver, Expression<Func<TSource, TSourceMember>> sourceMember);

        /// <summary>Maps using a member value resolver with a source member name.</summary>
        void MapFrom<TValueResolver, TSourceMember>(string sourceMemberName);

        /// <summary>Maps using a member value resolver with a source member expression.</summary>
        void MapFrom<TValueResolver, TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember);

        /// <summary>Maps using a member value resolver (auto source member).</summary>
        void MapFrom<TValueResolver>() where TValueResolver : IMemberValueResolver<TSource, TDestination, TMember, TMember>, new();

        /// <summary>Adds a condition.</summary>
        void Condition(Func<TSource, bool> condition);

        /// <summary>Adds a condition.</summary>
        void Condition(Func<TSource, TDestination, bool> condition);

        /// <summary>Adds a condition.</summary>
        void Condition(Func<TSource, TDestination, TMember, bool> condition);

        /// <summary>Adds a condition.</summary>
        void Condition(Func<TSource, TDestination, TMember, TMember, bool> condition);

        /// <summary>Adds a condition with context.</summary>
        void Condition(Func<TSource, TDestination, TMember, TMember, ResolutionContext, bool> condition);

        /// <summary>Adds a precondition.</summary>
        void PreCondition(Func<ResolutionContext, bool> condition);

        /// <summary>Adds a precondition.</summary>
        void PreCondition(Func<TSource, bool> condition);

        /// <summary>Adds a precondition.</summary>
        void PreCondition(Func<TSource, ResolutionContext, bool> condition);

        /// <summary>Adds a precondition.</summary>
        void PreCondition(Func<TSource, TDestination, ResolutionContext, bool> condition);

        /// <summary>Uses the destination's existing value.</summary>
        void UseDestinationValue();

        /// <summary>Does not use the destination's existing value.</summary>
        void DoNotUseDestinationValue();

        /// <summary>Maps at runtime.</summary>
        void MapAtRuntime();

        /// <summary>Sets the mapping order.</summary>
        void SetMappingOrder(int mappingOrder);

        /// <summary>Ignores the member.</summary>
        void Ignore();

        /// <summary>
        /// Resolves the member asynchronously. Only honored by <c>mapper.MapAsync(...)</c>;
        /// a synchronous <c>Map</c> leaves such members at their default.
        /// </summary>
        void MapFromAsync(IAsyncValueResolver<TSource, TDestination, TMember> asyncResolver);

        /// <summary>Resolves the member asynchronously from a function.</summary>
        void MapFromAsync(System.Func<TSource, System.Threading.Tasks.Task<TMember>> asyncResolver);

        /// <summary>Gets the destination member.</summary>
        MemberInfo DestinationMember { get; }
    }
}
