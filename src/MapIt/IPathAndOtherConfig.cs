using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// Parameters available to a condition function.
    /// </summary>
    public struct ConditionParameters<TSource, TDestination, TMember> : IEquatable<ConditionParameters<TSource, TDestination, TMember>>
    {
        /// <summary>Creates a condition parameters instance.</summary>
        public ConditionParameters(TSource source, TDestination destination, TMember sourceMember, TMember destinationMember, ResolutionContext context)
        {
            Source = source;
            Destination = destination;
            SourceMember = sourceMember;
            DestinationMember = destinationMember;
            Context = context;
        }

        /// <summary>The source.</summary>
        public TSource Source { get; set; }

        /// <summary>The destination.</summary>
        public TDestination Destination { get; set; }

        /// <summary>The source member value.</summary>
        public TMember SourceMember { get; set; }

        /// <summary>The destination member value.</summary>
        public TMember DestinationMember { get; set; }

        /// <summary>The resolution context.</summary>
        public ResolutionContext Context { get; set; }

        public void Deconstruct(out TSource Source, out TDestination Destination, out TMember SourceMember, out TMember DestinationMember, out ResolutionContext Context)
        {
            Source = this.Source;
            Destination = this.Destination;
            SourceMember = this.SourceMember;
            DestinationMember = this.DestinationMember;
            Context = this.Context;
        }

        public bool Equals(ConditionParameters<TSource, TDestination, TMember> other) =>
            Equals(Source, other.Source)
            && Equals(Destination, other.Destination);

        public override bool Equals(object obj) => obj is ConditionParameters<TSource, TDestination, TMember> o && Equals(o);

        public override int GetHashCode() => 0;

        public override string ToString() => "ConditionParameters";
    }

    /// <summary>
    /// Configuration for a nested destination path.
    /// </summary>
    public interface IPathConfigurationExpression<TSource, TDestination, TMember>
    {
        /// <summary>Maps the path from a source expression.</summary>
        void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember);

        /// <summary>Ignores the path.</summary>
        void Ignore();

        /// <summary>Adds a condition.</summary>
        void Condition(Func<ConditionParameters<TSource, TDestination, TMember>, bool> condition);
    }

    /// <summary>
    /// Configuration for a constructor parameter.
    /// </summary>
    public interface ICtorParamConfigurationExpression
    {
        /// <summary>Maps the parameter from a source member path.</summary>
        void MapFrom(string sourceMembersPath);

        /// <summary>Marks the parameter as an explicit expansion.</summary>
        void ExplicitExpansion();
    }

    /// <summary>
    /// Configuration for a constructor parameter with a known source type.
    /// </summary>
    public interface ICtorParamConfigurationExpression<TSource> : ICtorParamConfigurationExpression
    {
        /// <summary>Maps the parameter from a source member expression.</summary>
        void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember);

        /// <summary>Maps the parameter from a resolver function.</summary>
        void MapFrom<TMember>(Func<TSource, ResolutionContext, TMember> resolver);
    }

    /// <summary>
    /// Configuration for a source member.
    /// </summary>
    public interface ISourceMemberConfigurationExpression
    {
        /// <summary>Disables validation for this source member.</summary>
        void DoNotValidate();
    }

    /// <summary>
    /// A projection member configuration.
    /// </summary>
    public interface IProjectionMemberConfiguration<TSource, TDestination, TMember>
    {
        /// <summary>Adds a value transformer.</summary>
        void AddTransform(Expression<Func<TMember, TMember>> transformer);

        /// <summary>Allows null.</summary>
        void AllowNull();

        /// <summary>Does not allow null.</summary>
        void DoNotAllowNull();

        /// <summary>Marks as an explicit expansion.</summary>
        void ExplicitExpansion();

        /// <summary>Ignores the member.</summary>
        void Ignore();

        /// <summary>Maps from a source member expression.</summary>
        void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> mapExpression);

        /// <summary>Sets a null substitute.</summary>
        void NullSubstitute(object nullSubstitute);
    }

    /// <summary>
    /// A projection expression.
    /// </summary>
    public interface IProjectionExpressionBase<TSource, TDestination, TMappingExpression>
    {
        /// <summary>Configures construction with an expression.</summary>
        TMappingExpression ConstructUsing(Expression<Func<TSource, TDestination>> ctor);

        /// <summary>Configures a constructor parameter.</summary>
        TMappingExpression ForCtorParam(string ctorParamName, Action<ICtorParamConfigurationExpression<TSource>> paramOptions);

        /// <summary>Limits recursive mapping depth.</summary>
        TMappingExpression MaxDepth(int depth);

        /// <summary>Validates a member list.</summary>
        TMappingExpression ValidateMemberList(MemberList memberList);

        /// <summary>Uses a type converter expression.</summary>
        void ConvertUsing(Expression<Func<TSource, TDestination>> mappingExpression);
    }

    /// <summary>
    /// A projection expression with source/destination.
    /// </summary>
    public interface IProjectionExpression<TSource, TDestination, TMappingExpression> :
        IProjectionExpressionBase<TSource, TDestination, TMappingExpression>
    {
        /// <summary>Adds a value transformer.</summary>
        TMappingExpression AddTransform<TValue>(Expression<Func<TValue, TValue>> transformer);

        /// <summary>Includes member expressions.</summary>
        TMappingExpression IncludeMembers(Expression<Func<TSource, object>>[] memberExpressions);
    }

    /// <summary>
    /// A projection expression.
    /// </summary>
    public interface IProjectionExpression<TSource, TDestination> :
        IProjectionExpression<TSource, TDestination, IProjectionExpression<TSource, TDestination>>
    {
        /// <summary>Configures a destination member for projection.</summary>
        IProjectionExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IProjectionMemberConfiguration<TSource, TDestination, TMember>> memberOptions);
    }
}
