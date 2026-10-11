using System;

namespace MapIt
{
    /// <summary>
    /// Internal resolver representation for a member map.
    /// Encapsulates how a member value is resolved at runtime.
    /// </summary>
    public interface IValueResolverMember
    {
        /// <summary>
        /// Resolves the member value at runtime.
        /// </summary>
        /// <param name="source">The source object.</param>
        /// <param name="destination">The destination object.</param>
        /// <param name="destMember">The existing destination member value.</param>
        /// <param name="context">The resolution context.</param>
        /// <returns>The resolved value.</returns>
        object Resolve(object source, object destination, object destMember, ResolutionContext context);

        /// <summary>Whether this resolver requires a source member to be passed.</summary>
        bool RequiresSourceMember { get; }
    }

    /// <summary>
    /// Internal resolver for a user IValueResolver.
    /// </summary>
    public class ValueResolverMember : IValueResolverMember
    {
        internal ValueResolverMember(Func<object, object, object, ResolutionContext, object> resolve)
        {
            _resolve = resolve;
        }

        private readonly Func<object, object, object, ResolutionContext, object> _resolve;

        public bool RequiresSourceMember => false;

        public object Resolve(object source, object destination, object destMember, ResolutionContext context) =>
            _resolve(source, destination, destMember, context);
    }

    /// <summary>
    /// Internal resolver for a user IMemberValueResolver.
    /// </summary>
    public class MemberValueResolverMember : IValueResolverMember
    {
        internal MemberValueResolverMember(
            Func<object, object, object, object, ResolutionContext, object> resolve,
            Func<object, object> sourceMemberAccessor)
        {
            _resolve = resolve;
            SourceMemberAccessor = sourceMemberAccessor;
        }

        private readonly Func<object, object, object, object, ResolutionContext, object> _resolve;

        /// <summary>A function that extracts the source member value from the source.</summary>
        public Func<object, object> SourceMemberAccessor { get; }

        public bool RequiresSourceMember => true;

        public object Resolve(object source, object destination, object destMember, ResolutionContext context)
        {
            var sourceMemberValue = SourceMemberAccessor != null ? SourceMemberAccessor(source) : destMember;
            return _resolve(source, destination, sourceMemberValue, destMember, context);
        }
    }

    /// <summary>
    /// Internal resolver for a value converter applied to a member.
    /// </summary>
    public class ValueConverterMember : IValueResolverMember
    {
        internal ValueConverterMember(
            Func<object, object, object, object, ResolutionContext, object> resolve)
        {
            _resolve = resolve;
        }

        private readonly Func<object, object, object, object, ResolutionContext, object> _resolve;

        public bool RequiresSourceMember => true;

        public object Resolve(object source, object destination, object destMember, ResolutionContext context)
        {
            // The engine resolves the source member chain and passes it as destMember.
            return _resolve(source, destination, destMember, destMember, context);
        }
    }
}
