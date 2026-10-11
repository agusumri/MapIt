using System;

namespace MapIt
{
    /// <summary>
    /// A cross-cutting interceptor invoked around every top-level mapping operation. Register one
    /// or more via <c>cfg.AddInterceptor(...)</c>. Useful for auditing, logging, metrics, or
    /// enrichment without touching individual maps.
    /// </summary>
    public interface IMappingInterceptor
    {
        /// <summary>
        /// Called before a top-level map runs. The returned value, if non-null, short-circuits the
        /// operation and is returned to the caller as the mapping result.
        /// </summary>
        object OnBeforeMap(MappingInterceptionContext context);

        /// <summary>
        /// Called after a top-level map completes. May return a replacement result; returning the
        /// supplied <paramref name="result"/> leaves it unchanged.
        /// </summary>
        object OnAfterMap(MappingInterceptionContext context, object result);
    }

    /// <summary>Context passed to a mapping interceptor.</summary>
    public sealed class MappingInterceptionContext
    {
        /// <summary>Creates an interception context.</summary>
        public MappingInterceptionContext(object source, Type sourceType, Type destinationType, ResolutionContext resolutionContext)
        {
            Source = source;
            SourceType = sourceType;
            DestinationType = destinationType;
            ResolutionContext = resolutionContext;
        }

        /// <summary>The source object.</summary>
        public object Source { get; }

        /// <summary>The declared source type.</summary>
        public Type SourceType { get; }

        /// <summary>The destination type.</summary>
        public Type DestinationType { get; }

        /// <summary>The active resolution context.</summary>
        public ResolutionContext ResolutionContext { get; }
    }

    /// <summary>
    /// A convenience base class so interceptors can override only the hook they need.
    /// </summary>
    public abstract class MappingInterceptor : IMappingInterceptor
    {
        /// <inheritdoc />
        public virtual object OnBeforeMap(MappingInterceptionContext context) => null;

        /// <inheritdoc />
        public virtual object OnAfterMap(MappingInterceptionContext context, object result) => result;
    }
}
