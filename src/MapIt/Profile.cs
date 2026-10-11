using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A mapping profile: a set of type maps and their configurations.
    /// </summary>
    public class Profile : IProfileExpression
    {
        /// <summary>Creates a default profile.</summary>
        public Profile() : this("<default>") { }

        /// <summary>Creates a named profile.</summary>
        public Profile(string name)
        {
            ProfileName = name;
        }

        #region IProfileExpression

        /// <inheritdoc />
        public virtual string ProfileName { get; }

        internal List<TypeMap> TypeMaps { get; } = new List<TypeMap>();
        internal List<string> GlobalIgnores { get; } = new List<string>();
        // Source-side and destination-side prefix/postfix recognition are tracked separately.
        internal List<string> SourcePrefixes { get; } = new List<string>();
        internal List<string> SourcePostfixes { get; } = new List<string>();
        internal List<string> DestinationPrefixes { get; } = new List<string>();
        internal List<string> DestinationPostfixes { get; } = new List<string>();
        internal List<Tuple<string, string>> MemberNameReplacers { get; } = new List<Tuple<string, string>>();
        internal List<Type> SourceExtensionMethodTypes { get; } = new List<Type>();
        internal List<Func<PropertyMap, IMemberConfigurationExpression>> AllPropertyMapActions { get; } = new List<Func<PropertyMap, IMemberConfigurationExpression>>();
        internal List<Action<TypeMap, IMappingExpression>> AllTypeMapActions { get; } = new List<Action<TypeMap, IMappingExpression>>();
        internal bool ConstructorMappingEnabled = true;
        internal bool FieldMappingEnabled = true;
        internal bool MethodMappingEnabled = false;

        /// <inheritdoc />
        public bool? AllowNullCollections { get; set; }
        /// <inheritdoc />
        public bool? AllowNullDestinationValues { get; set; }
        /// <inheritdoc />
        public INamingConvention DestinationMemberNamingConvention { get; set; } = new ExactMatchNamingConvention();
        /// <inheritdoc />
        public INamingConvention SourceMemberNamingConvention { get; set; } = new ExactMatchNamingConvention();
        /// <inheritdoc />
        public Func<FieldInfo, bool> ShouldMapField { get; set; }
        /// <inheritdoc />
        public Func<MethodInfo, bool> ShouldMapMethod { get; set; }
        /// <inheritdoc />
        public Func<PropertyInfo, bool> ShouldMapProperty { get; set; }
        /// <inheritdoc />
        public Func<ConstructorInfo, bool> ShouldUseConstructor { get; set; }
        /// <inheritdoc />
        public List<ValueTransformerConfiguration> ValueTransformers { get; } = new List<ValueTransformerConfiguration>();

        /// <inheritdoc />
        public IMappingExpression CreateMap(Type sourceType, Type destinationType)
            => CreateMap(sourceType, destinationType, MemberList.Destination);

        /// <inheritdoc />
        public IMappingExpression CreateMap(Type sourceType, Type destinationType, MemberList memberList)
        {
            var typeMap = new TypeMap(sourceType, destinationType, memberList);
            if (typeMap.IsOpenGeneric)
                typeMap.OpenGenericTypeMap = new OpenGenericTypeMap(sourceType, destinationType);
            TypeMaps.Add(typeMap);
            return new MappingExpression(typeMap, this);
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
        {
            var typeMap = new TypeMap(typeof(TSource), typeof(TDestination), MemberList.Destination);
            TypeMaps.Add(typeMap);
            return new MappingExpression<TSource, TDestination>(typeMap, this);
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(MemberList memberList)
        {
            var typeMap = new TypeMap(typeof(TSource), typeof(TDestination), memberList);
            TypeMaps.Add(typeMap);
            return new MappingExpression<TSource, TDestination>(typeMap, this);
        }

        /// <inheritdoc />
        public IProjectionExpression<TSource, TDestination> CreateProjection<TSource, TDestination>()
        {
            var typeMap = new TypeMap(typeof(TSource), typeof(TDestination), MemberList.Destination);
            TypeMaps.Add(typeMap);
            return (IProjectionExpression<TSource, TDestination>)new MappingExpression<TSource, TDestination>(typeMap, this);
        }

        /// <inheritdoc />
        public IProjectionExpression<TSource, TDestination> CreateProjection<TSource, TDestination>(MemberList memberList)
        {
            var typeMap = new TypeMap(typeof(TSource), typeof(TDestination), memberList);
            TypeMaps.Add(typeMap);
            return (IProjectionExpression<TSource, TDestination>)new MappingExpression<TSource, TDestination>(typeMap, this);
        }

        /// <inheritdoc />
        public void AddGlobalIgnore(string propertyNameStartingWith) => GlobalIgnores.Add(propertyNameStartingWith);

        /// <inheritdoc />
        public void ClearPrefixes() => SourcePrefixes.Clear();

        /// <inheritdoc />
        public void DisableConstructorMapping() => ConstructorMappingEnabled = false;

        /// <inheritdoc />
        public void IncludeSourceExtensionMethods(Type type)
        {
            if (type != null) SourceExtensionMethodTypes.Add(type);
        }

        /// <inheritdoc />
        public void RecognizeDestinationPostfixes(string[] postfixes)
        {
            if (postfixes != null) DestinationPostfixes.AddRange(postfixes);
        }

        /// <inheritdoc />
        public void RecognizeDestinationPrefixes(string[] prefixes)
        {
            if (prefixes != null) DestinationPrefixes.AddRange(prefixes);
        }

        /// <inheritdoc />
        public void RecognizePostfixes(string[] postfixes)
        {
            if (postfixes != null) SourcePostfixes.AddRange(postfixes);
        }

        /// <inheritdoc />
        public void RecognizePrefixes(string[] prefixes)
        {
            if (prefixes != null) SourcePrefixes.AddRange(prefixes);
        }

        /// <inheritdoc />
        public void ReplaceMemberName(string original, string newValue)
        {
            if (!string.IsNullOrEmpty(original))
                MemberNameReplacers.Add(Tuple.Create(original, newValue ?? string.Empty));
        }

        #endregion
    }
}
