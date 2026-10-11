using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A member-config expression used when configuring an OPEN-generic map. Instead of binding
    /// to open-definition MemberInfos, it records the configuration as a data recipe to be
    /// replayed against the closed type map (BEHAVIOR §2 open-generic parity).
    /// </summary>
    internal sealed class RecordingMemberConfig :
        IMemberConfigurationExpression,
        IProjectionMemberConfiguration<object, object, object>
    {
        public OpenValueRecipe Recipe { get; } = new OpenValueRecipe();

        public MemberInfo DestinationMember => null;

        public void MapFrom(Type valueResolverType) => Recipe.ResolverType = valueResolverType;
        public void MapFrom(Type valueResolverType, string sourceMemberName)
        {
            Recipe.ResolverType = valueResolverType;
            Recipe.SourceMemberNames = sourceMemberName?.Split('.');
        }
        public void MapFrom<TSource, TDestination, TSourceMember, TDestMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TDestMember> resolver, string sourceMemberName)
            => throw OpenNotSupported("MapFrom(resolver-instance)");

        public void ConvertUsing(Type valueConverterType) => Recipe.ConverterType = valueConverterType;
        public void ConvertUsing(Type valueConverterType, string sourceMemberName)
        {
            Recipe.ConverterType = valueConverterType;
            Recipe.SourceMemberNames = sourceMemberName?.Split('.');
        }
        public void ConvertUsing<TSourceMember, TDestinationMember>(IValueConverter<TSourceMember, TDestinationMember> valueConverter, string sourceMemberName)
            => throw OpenNotSupported("ConvertUsing(converter-instance)");

        // IProjectionMemberConfiguration<object,object,object>
        public void AddTransform(Expression<Func<object, object>> transformer) { }
        public void AllowNull() { }
        public void DoNotAllowNull() { }
        public void ExplicitExpansion() { }
        public void Ignore() => Recipe.Ignore = true;
        public void MapFrom<TSourceMember>(Expression<Func<object, TSourceMember>> mapExpression)
            => Recipe.RawExpression = mapExpression;
        public void NullSubstitute(object nullSubstitute) => Recipe.NullSubstitute = nullSubstitute;

        private static MapItMappingException OpenNotSupported(string what) =>
            new MapItMappingException($"The open-generic configuration step '{what}' with a captured instance cannot be rebound to closed types. Use a resolver/converter type or an expression instead.");
    }

    /// <summary>Typed member-config recorder for open-generic ForMember&lt;TMember&gt;.</summary>
    internal sealed class TypedRecordingMemberConfig<TSource, TDestination, TMember> :
        IMemberConfigurationExpression<TSource, TDestination, TMember>
    {
        private readonly RecordingMemberConfig _inner;
        public TypedRecordingMemberConfig(RecordingMemberConfig inner) => _inner = inner;
        private OpenValueRecipe Recipe => _inner.Recipe;

        public MemberInfo DestinationMember => null;

        // Base IMemberConfigurationExpression members.
        void IMemberConfigurationExpression.MapFrom(Type valueResolverType) => Recipe.ResolverType = valueResolverType;
        void IMemberConfigurationExpression.MapFrom(Type valueResolverType, string sourceMemberName) { Recipe.ResolverType = valueResolverType; Recipe.SourceMemberNames = sourceMemberName?.Split('.'); }
        void IMemberConfigurationExpression.MapFrom<TS, TD, TSM, TDM>(IMemberValueResolver<TS, TD, TSM, TDM> resolver, string sourceMemberName) => throw RuntimeDelegate();
        void IMemberConfigurationExpression.ConvertUsing(Type valueConverterType) => Recipe.ConverterType = valueConverterType;
        void IMemberConfigurationExpression.ConvertUsing(Type valueConverterType, string sourceMemberName) { Recipe.ConverterType = valueConverterType; Recipe.SourceMemberNames = sourceMemberName?.Split('.'); }
        void IMemberConfigurationExpression.ConvertUsing<TSM, TDM>(IValueConverter<TSM, TDM> valueConverter, string sourceMemberName) => throw RuntimeDelegate();

        public void MapFrom(string sourceMembersPath) => Recipe.SourceMemberNames = sourceMembersPath?.Split('.');
        public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> mapExpression) => Recipe.RawExpression = mapExpression;
        public void MapFrom<TResult>(Func<TSource, TDestination, TResult> mappingFunction) => throw RuntimeDelegate();
        public void MapFrom<TResult>(Func<TSource, TDestination, TMember, TResult> mappingFunction) => throw RuntimeDelegate();
        public void MapFrom<TResult>(Func<TSource, TDestination, TMember, ResolutionContext, TResult> mappingFunction) => throw RuntimeDelegate();
        public void MapFrom(IValueResolver<TSource, TDestination, TMember> valueResolver) => throw RuntimeDelegate();
        public void MapFrom<TSourceMember>(IMemberValueResolver<TSource, TDestination, TSourceMember, TMember> valueResolver, Expression<Func<TSource, TSourceMember>> sourceMember) => throw RuntimeDelegate();
        public void MapFrom<TValueResolver, TSourceMember>(string sourceMemberName) { Recipe.ResolverType = typeof(TValueResolver); Recipe.SourceMemberNames = sourceMemberName?.Split('.'); }
        public void MapFrom<TValueResolver, TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember) { Recipe.ResolverType = typeof(TValueResolver); }
        public void MapFrom<TValueResolver>() where TValueResolver : IValueResolver<TSource, TDestination, TMember> => Recipe.ResolverType = typeof(TValueResolver);

        public void Condition(Func<TSource, bool> condition) => throw RuntimeDelegate();
        public void Condition(Func<TSource, TDestination, bool> condition) => throw RuntimeDelegate();
        public void Condition(Func<TSource, TDestination, TMember, bool> condition) => throw RuntimeDelegate();
        public void Condition(Func<TSource, TDestination, TMember, TMember, bool> condition) => throw RuntimeDelegate();
        public void Condition(Func<TSource, TDestination, TMember, TMember, ResolutionContext, bool> condition) => throw RuntimeDelegate();
        public void PreCondition(Func<ResolutionContext, bool> condition) => throw RuntimeDelegate();
        public void PreCondition(Func<TSource, bool> condition) => throw RuntimeDelegate();
        public void PreCondition(Func<TSource, ResolutionContext, bool> condition) => throw RuntimeDelegate();
        public void PreCondition(Func<TSource, TDestination, ResolutionContext, bool> condition) => throw RuntimeDelegate();

        public void UseDestinationValue() => Recipe.UseDestinationValue = true;
        public void DoNotUseDestinationValue() => Recipe.UseDestinationValue = false;
        public void MapAtRuntime() { }
        public void SetMappingOrder(int mappingOrder) => Recipe.MappingOrder = mappingOrder;
        public void NullSubstitute(object nullSubstitute) => Recipe.NullSubstitute = nullSubstitute;
        public void AllowNull() => Recipe.AllowNull = true;
        public void DoNotAllowNull() => Recipe.AllowNull = false;
        public void ExplicitExpansion() => Recipe.ExplicitExpansion = true;
        public void AddTransform(Expression<Func<TMember, TMember>> transformer) => throw RuntimeDelegate();
        public void MapFromAsync(IAsyncValueResolver<TSource, TDestination, TMember> asyncResolver) => throw RuntimeDelegate();
        public void MapFromAsync(System.Func<TSource, System.Threading.Tasks.Task<TMember>> asyncResolver) => throw RuntimeDelegate();

        public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> valueConverter, Expression<Func<TSource, TSourceMember>> sourceMember) => throw RuntimeDelegate();
        public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> valueConverter, string sourceMemberName) => throw RuntimeDelegate();
        public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> valueConverter) => throw RuntimeDelegate();
        public void ConvertUsing<TValueConverter, TSourceMember>() where TValueConverter : IValueConverter<TSourceMember, TMember> => Recipe.ConverterType = typeof(TValueConverter);
        public void ConvertUsing<TValueConverter, TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember) where TValueConverter : IValueConverter<TSourceMember, TMember> => Recipe.ConverterType = typeof(TValueConverter);
        public void ConvertUsing<TValueConverter, TSourceMember>(string sourceMemberName) where TValueConverter : IValueConverter<TSourceMember, TMember> { Recipe.ConverterType = typeof(TValueConverter); Recipe.SourceMemberNames = sourceMemberName?.Split('.'); }

        public void Ignore() => Recipe.Ignore = true;

        private static MapItMappingException RuntimeDelegate() =>
            new MapItMappingException("Open-generic ForMember with a runtime delegate/resolver-instance cannot be rebound; use MapFrom(name)/MapFrom(expression)/a resolver type.");
    }

    /// <summary>Records an open-generic ForPath configuration as a recipe.</summary>
    internal sealed class RecordingPathConfig<TSource, TDestination, TMember> : IPathConfigurationExpression<TSource, TDestination, TMember>
    {
        public OpenValueRecipe Recipe { get; } = new OpenValueRecipe();
        public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember) => Recipe.RawExpression = sourceMember;
        public void Ignore() => Recipe.Ignore = true;
        public void Condition(Func<ConditionParameters<TSource, TDestination, TMember>, bool> condition)
            => throw new MapItMappingException("Open-generic ForPath with a runtime condition cannot be rebound.");
    }
}
