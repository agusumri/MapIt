using System;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// Base mapping expression shared by all map types.
    /// </summary>
    public interface IMappingExpressionBase<TSource, TDestination, TMappingExpression>
    {
        /// <summary>Configures how to construct the destination.</summary>
        TMappingExpression ConstructUsing(Func<TSource, ResolutionContext, TDestination> ctor);

        /// <summary>Configures construction using the service locator.</summary>
        TMappingExpression ConstructUsingServiceLocator();

        /// <summary>Disables constructor validation.</summary>
        TMappingExpression DisableCtorValidation();

        /// <summary>Registers a before-map action.</summary>
        TMappingExpression BeforeMap(Action<TSource, TDestination> beforeFunction);

        /// <summary>Registers a before-map action with context.</summary>
        TMappingExpression BeforeMap(Action<TSource, TDestination, ResolutionContext> beforeFunction);

        /// <summary>Registers a before-map action of a type.</summary>
        TMappingExpression BeforeMap<TMappingAction>() where TMappingAction : IMappingAction<TSource, TDestination>, new();

        /// <summary>Registers an after-map action.</summary>
        TMappingExpression AfterMap(Action<TSource, TDestination> afterFunction);

        /// <summary>Registers an after-map action with context.</summary>
        TMappingExpression AfterMap(Action<TSource, TDestination, ResolutionContext> afterFunction);

        /// <summary>Registers an after-map action of a type.</summary>
        TMappingExpression AfterMap<TMappingAction>() where TMappingAction : IMappingAction<TSource, TDestination>, new();

        /// <summary>Specifies that the map produces a proxy.</summary>
        TMappingExpression AsProxy();

        /// <summary>Includes a derived type map.</summary>
        TMappingExpression Include(Type derivedSourceType, Type derivedDestinationType);

        /// <summary>Includes all derived types.</summary>
        TMappingExpression IncludeAllDerived();

        /// <summary>Includes base types.</summary>
        TMappingExpression IncludeBase(Type sourceBase, Type destinationBase);

        /// <summary>Limits recursive mapping depth.</summary>
        TMappingExpression MaxDepth(int depth);

        /// <summary>Preserves references to avoid infinite recursion.</summary>
        TMappingExpression PreserveReferences();

        /// <summary>Validates a member list.</summary>
        TMappingExpression ValidateMemberList(MemberList memberList);

        /// <summary>Overrides the destination type.</summary>
        void As(Type typeOverride);

        /// <summary>Uses a type converter for the whole map.</summary>
        void ConvertUsing(ITypeConverter<TSource, TDestination> converter);

        /// <summary>Uses a mapping function for the whole map.</summary>
        void ConvertUsing(Func<TSource, TDestination, TDestination> mappingFunction);

        /// <summary>Uses a mapping function with context for the whole map.</summary>
        void ConvertUsing(Func<TSource, TDestination, ResolutionContext, TDestination> mappingFunction);

        /// <summary>Uses a type converter from a type.</summary>
        void ConvertUsing(Type typeConverterType);

        /// <summary>Uses a typed type converter.</summary>
        void ConvertUsing<TTypeConverter>() where TTypeConverter : ITypeConverter<TSource, TDestination>, new();

        /// <summary>Configures a constructor parameter.</summary>
        TMappingExpression ForCtorParam(string ctorParamName, Action<ICtorParamConfigurationExpression<TSource>> paramOptions);

        /// <summary>Configures a source member that should not be mapped.</summary>
        TMappingExpression ForSourceMember(string sourceMemberName, Action<ISourceMemberConfigurationExpression> memberOptions);

        /// <summary>Builds an expression for the destination.</summary>
        TMappingExpression ConstructUsing(Expression<Func<TSource, TDestination>> ctor);
    }

    /// <summary>
    /// A mapping expression for a map with known source and destination types.
    /// </summary>
    public interface IMappingExpression<TSource, TDestination> :
        IMappingExpressionBase<TSource, TDestination, IMappingExpression<TSource, TDestination>>
    {
        /// <summary>Creates a reverse map.</summary>
        IMappingExpression<TDestination, TSource> ReverseMap();

        /// <summary>Configures a destination member by name.</summary>
        IMappingExpression<TSource, TDestination> ForMember(string name, Action<IMemberConfigurationExpression<TSource, TDestination, object>> memberOptions);

        /// <summary>Configures a destination member by expression.</summary>
        IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> memberOptions);

        /// <summary>Configures a destination path.</summary>
        IMappingExpression<TSource, TDestination> ForPath<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IPathConfigurationExpression<TSource, TDestination, TMember>> memberOptions);

        /// <summary>Configures a source member.</summary>
        IMappingExpression<TSource, TDestination> ForSourceMember(Expression<Func<TSource, object>> sourceMember, Action<ISourceMemberConfigurationExpression> memberOptions);

        /// <summary>Includes a derived type pair.</summary>
        IMappingExpression<TSource, TDestination> Include<TOtherSource, TOtherDestination>() where TOtherSource : TSource where TOtherDestination : TDestination;

        /// <summary>Includes a base type pair.</summary>
        IMappingExpression<TSource, TDestination> IncludeBase<TSourceBase, TDestinationBase>() where TSourceBase : class where TDestinationBase : class;

        /// <summary>Overrides the destination type.</summary>
        void As<T>() where T : TDestination;

        /// <summary>Flattens members from the given source members into this destination.</summary>
        IMappingExpression<TSource, TDestination> IncludeMembers(params Expression<Func<TSource, object>>[] memberExpressions);

        /// <summary>
        /// Matches source items to existing destination items by key when mapping into an existing
        /// destination collection (updates matched items in place, adds new, removes missing).
        /// </summary>
        IMappingExpression<TSource, TDestination> EqualityComparison(Expression<Func<TSource, TDestination, bool>> comparison);

        /// <summary>
        /// Configures enum-to-enum mapping with explicit value overrides.
        /// Unmapped values fall back to name then numeric matching.
        /// </summary>
        IMappingExpression<TSource, TDestination> ConvertUsingEnumMapping(Action<IEnumMappingExpression<TSource, TDestination>> options = null);

        /// <summary>Configures all members.</summary>
        void ForAllMembers(Action<IMemberConfigurationExpression<TSource, TDestination, object>> memberOptions);
    }

    /// <summary>
    /// A mapping expression for a map with untyped source/destination.
    /// </summary>
    public interface IMappingExpression :
        IMappingExpressionBase<object, object, IMappingExpression>
    {
        /// <summary>Creates a reverse map.</summary>
        IMappingExpression ReverseMap();

        /// <summary>Configures a destination member by name.</summary>
        IMappingExpression ForMember(string name, Action<IMemberConfigurationExpression> memberOptions);

        /// <summary>Configures all members.</summary>
        void ForAllMembers(Action<IMemberConfigurationExpression> memberOptions);

        /// <summary>Includes member names.</summary>
        IMappingExpression IncludeMembers(string[] memberNames);
    }
}
