using System;

namespace MapIt
{
    /// <summary>
    /// Resolves a destination member value from a source.
    /// </summary>
    public interface IValueResolver<TSource, TDestination, TDestMember>
    {
        /// <summary>
        /// Resolves the destination member value.
        /// </summary>
        TDestMember Resolve(TSource source, TDestination destination, TDestMember destMember, ResolutionContext context);
    }

    /// <summary>
    /// Resolves a destination member value from a specific source member.
    /// </summary>
    public interface IMemberValueResolver<TSource, TDestination, TSourceMember, TDestMember>
    {
        /// <summary>
        /// Resolves the destination member value from the given source member.
        /// </summary>
        TDestMember Resolve(TSource source, TDestination destination, TSourceMember sourceMember, TDestMember destMember, ResolutionContext context);
    }

    /// <summary>
    /// Converts an entire object from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>.
    /// </summary>
    public interface ITypeConverter<TSource, TDestination>
    {
        /// <summary>
        /// Converts the source to a destination instance.
        /// </summary>
        TDestination Convert(TSource source, TDestination destination, ResolutionContext context);
    }

    /// <summary>
    /// Converts a source member value to a destination member value.
    /// </summary>
    public interface IValueConverter<TSourceMember, TDestinationMember>
    {
        /// <summary>
        /// Converts the source member to a destination member value.
        /// </summary>
        TDestinationMember Convert(TSourceMember sourceMember, ResolutionContext context);
    }

    /// <summary>
    /// Performs a custom action on source/destination after mapping.
    /// </summary>
    public interface IMappingAction<TSource, TDestination>
    {
        /// <summary>
        /// Processes the source and destination.
        /// </summary>
        void Process(TSource source, TDestination destination, ResolutionContext context);
    }
}
