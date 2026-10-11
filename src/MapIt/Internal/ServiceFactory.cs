using System;

namespace MapIt.Internal
{
    /// <summary>
    /// Creates resolver, converter and action types referenced by type, through the
    /// configured service constructor (DI) when present, else via <see cref="Activator"/>.
    /// </summary>
    internal static class ServiceFactory
    {
        public static T Create<T>(ResolutionContext context) => (T)Create(typeof(T), context);

        public static object Create(Type type, ResolutionContext context)
        {
            var ctor = context?.ServiceCtor;
            var instance = ctor != null ? ctor(type) : null;
            return instance ?? Activator.CreateInstance(type);
        }
    }
}
