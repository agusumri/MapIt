using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MapIt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MapIt.Extensions.Microsoft.DependencyInjection
{
    /// <summary>
    /// Extensions for adding MapIt to an <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds MapIt to the service collection with configuration.
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configAction)
            => AddMapIt(services, configAction, ServiceLifetime.Singleton);

        private static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configAction,
            ServiceLifetime mapperLifetime)
        {
            // Config is sealed in the constructor; no warm-up CreateMapper() needed.
            var configuration = new MapperConfiguration(configAction);
            services.TryAddSingleton<IConfigurationProvider>(configuration);
            AddMapperWithLifetime(services, mapperLifetime);
            return services;
        }

        /// <summary>
        /// Registers the mapper. Resolvers, converters and mapping actions referenced by type are
        /// created through the container (constructor injection), falling back to
        /// <see cref="ActivatorUtilities"/> for types that are not registered.
        /// A scoped or transient mapper resolves those services from the current scope.
        /// </summary>
        private static void AddMapperWithLifetime(IServiceCollection services, ServiceLifetime lifetime)
        {
            services.TryAddSingleton<Mapper>(sp => CreateMapper(sp));
            if (lifetime == ServiceLifetime.Singleton)
            {
                services.TryAdd(new ServiceDescriptor(typeof(IMapper), sp => sp.GetRequiredService<Mapper>(), lifetime));
                services.TryAdd(new ServiceDescriptor(typeof(IRuntimeMapper), sp => sp.GetRequiredService<Mapper>(), lifetime));
            }
            else
            {
                services.TryAdd(new ServiceDescriptor(typeof(IMapper), sp => CreateMapper(sp), lifetime));
                services.TryAdd(new ServiceDescriptor(typeof(IRuntimeMapper), sp => (IRuntimeMapper)sp.GetRequiredService<IMapper>(), lifetime));
            }
        }

        private static Mapper CreateMapper(IServiceProvider sp)
        {
            var configuration = (MapperConfiguration)sp.GetRequiredService<IConfigurationProvider>();
            return (Mapper)configuration.CreateMapper(type => sp.GetService(type) ?? ActivatorUtilities.CreateInstance(sp, type));
        }

        /// <summary>
        /// Adds MapIt to the service collection with configuration and service provider.
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IServiceProvider, IMapperConfigurationExpression> configAction)
        {
            services.TryAddSingleton<IConfigurationProvider>(sp =>
            {
                var config = new MapperConfigurationExpression();
                configAction(sp, config);
                return new MapperConfiguration(config);
            });
            AddMapperWithLifetime(services, ServiceLifetime.Singleton);
            return services;
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning assemblies for maps.
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configAction,
            Assembly[] assemblies)
        {
            return services.AddMapIt(config =>
            {
                configAction(config);
                config.AddMaps(assemblies);
            });
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning profile marker types' assemblies.
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configAction,
            Type[] profileAssemblyMarkerTypes)
        {
            return services.AddMapIt(config =>
            {
                configAction(config);
                config.AddMaps(profileAssemblyMarkerTypes.Select(t => t.GetTypeInfo().Assembly).Distinct().ToArray());
            });
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning assemblies with a service lifetime.
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configAction,
            IEnumerable<Assembly> assemblies,
            ServiceLifetime serviceLifetime = ServiceLifetime.Singleton)
        {
            return services.AddMapIt(config =>
            {
                configAction(config);
                config.AddMaps(assemblies);
            }, serviceLifetime);
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning profile marker types with a service lifetime.
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configAction,
            IEnumerable<Type> profileAssemblyMarkerTypes,
            ServiceLifetime serviceLifetime = ServiceLifetime.Singleton)
        {
            return services.AddMapIt(config =>
            {
                configAction(config);
                config.AddMaps(profileAssemblyMarkerTypes.Select(t => t.GetTypeInfo().Assembly).Distinct().ToArray());
            }, serviceLifetime);
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning assemblies (no config action).
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            params Assembly[] assemblies)
        {
            return services.AddMapIt(config => config.AddMaps(assemblies));
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning profile marker types (no config action).
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            params Type[] profileAssemblyMarkerTypes)
        {
            return services.AddMapIt(config => config.AddMaps(
                profileAssemblyMarkerTypes.Select(t => t.GetTypeInfo().Assembly).Distinct().ToArray()));
        }

        /// <summary>
        /// Adds MapIt to the service collection, scanning assemblies with a service lifetime (no config action).
        /// </summary>
        public static IServiceCollection AddMapIt(
            this IServiceCollection services,
            IEnumerable<Assembly> assemblies,
            ServiceLifetime serviceLifetime = ServiceLifetime.Singleton)
        {
            return services.AddMapIt(config => config.AddMaps(assemblies), serviceLifetime);
        }
    }
}
