using System;
using System.Collections.Generic;

namespace MapIt
{
    /// <summary>
    /// Options for a mapping operation.
    /// </summary>
    public interface IMappingOperationOptions
    {
        /// <summary>
        /// Gets or sets the per-operation user items.
        /// </summary>
        Dictionary<string, object> Items { get; }

        /// <summary>
        /// Gets or sets the service constructor used to build resolvers/converters.
        /// </summary>
        Func<Type, object> ServiceCtor { get; }

        /// <summary>
        /// Registers a before-map action.
        /// </summary>
        void BeforeMap(Action<object, object> beforeFunction);

        /// <summary>
        /// Registers an after-map action.
        /// </summary>
        void AfterMap(Action<object, object> afterFunction);

        /// <summary>
        /// Sets the service constructor.
        /// </summary>
        void ConstructServicesUsing(Func<Type, object> constructor);
    }

    /// <summary>
    /// Options for a mapping operation with typed source/destination.
    /// </summary>
    public interface IMappingOperationOptions<TSource, TDestination> : IMappingOperationOptions
    {
        /// <summary>
        /// Registers a typed before-map action.
        /// </summary>
        void BeforeMap(Action<TSource, TDestination> beforeFunction);

        /// <summary>
        /// Registers a typed after-map action.
        /// </summary>
        void AfterMap(Action<TSource, TDestination> afterFunction);
    }

    /// <summary>
    /// Internal access to operation-scoped before/after actions and the service-ctor override.
    /// </summary>
    internal interface IMappingOperationOptionsInternal
    {
        Func<Type, object> ServiceCtorOverride { get; }
        void RunBeforeMap(object source, object destination, ResolutionContext context);
        void RunAfterMap(object source, object destination, ResolutionContext context);
    }

    /// <summary>
    /// Concrete mapping operation options.
    /// </summary>
    public class MappingOperationOptions<TSource, TDestination> : IMappingOperationOptions<TSource, TDestination>, IMappingOperationOptions, IMappingOperationOptionsInternal
    {
        /// <inheritdoc />
        public Dictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public Func<Type, object> ServiceCtor { get; set; }

        internal Action<TSource, TDestination> BeforeMapAction { get; set; }
        internal Action<TSource, TDestination> AfterMapAction { get; set; }
        internal List<Action<object, object>> BeforeMapActions { get; } = new List<Action<object, object>>();
        internal List<Action<object, object>> AfterMapActions { get; } = new List<Action<object, object>>();

        /// <inheritdoc />
        public void BeforeMap(Action<TSource, TDestination> beforeFunction) => BeforeMapAction = beforeFunction;

        /// <inheritdoc />
        public void AfterMap(Action<TSource, TDestination> afterFunction) => AfterMapAction = afterFunction;

        /// <inheritdoc />
        void IMappingOperationOptions.BeforeMap(Action<object, object> beforeFunction) => BeforeMapActions.Add(beforeFunction);

        /// <inheritdoc />
        void IMappingOperationOptions.AfterMap(Action<object, object> afterFunction) => AfterMapActions.Add(afterFunction);

        /// <inheritdoc />
        public void ConstructServicesUsing(Func<Type, object> constructor) => ServiceCtor = constructor;

        Func<Type, object> IMappingOperationOptionsInternal.ServiceCtorOverride => ServiceCtor;

        void IMappingOperationOptionsInternal.RunBeforeMap(object source, object destination, ResolutionContext context)
        {
            if (BeforeMapAction != null) BeforeMapAction((TSource)source, (TDestination)(destination ?? default(TDestination)));
            foreach (var a in BeforeMapActions) a(source, destination);
        }

        void IMappingOperationOptionsInternal.RunAfterMap(object source, object destination, ResolutionContext context)
        {
            if (AfterMapAction != null) AfterMapAction((TSource)source, (TDestination)(destination ?? default(TDestination)));
            foreach (var a in AfterMapActions) a(source, destination);
        }
    }
}
