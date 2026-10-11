using System;
using System.Collections.Generic;
using System.Linq;

namespace MapIt
{
    /// <summary>
    /// A sealed profile map used by the configuration.
    /// </summary>
    public class ProfileMap
    {
        internal ProfileMap(Profile profile, Profile root)
        {
            Profile = profile;
            Name = profile.ProfileName;
            // Resolve inheritance once, at seal (S6): declaring profile -> root <global> -> default.
            var fallback = ReferenceEquals(profile, root) ? null : root;
            AllowNullCollections = profile.AllowNullCollections ?? fallback?.AllowNullCollections ?? false;
            AllowNullDestinationValues = profile.AllowNullDestinationValues ?? fallback?.AllowNullDestinationValues ?? true;
        }

        internal Profile Profile { get; }

        /// <summary>Gets the profile name.</summary>
        public string Name { get; }

        /// <summary>Whether null collections are allowed (BEHAVIOR §5 step 8 / §10, default false).</summary>
        public bool AllowNullCollections { get; }

        /// <summary>Whether null destination values are allowed (default true).</summary>
        public bool AllowNullDestinationValues { get; }

        /// <summary>Gets or sets the member configuration.</summary>
        public MemberConfiguration MemberConfiguration { get; set; }

        /// <summary>Gets the global ignores.</summary>
        public HashSet<string> GlobalIgnores => new HashSet<string>(Profile.GlobalIgnores);

        /// <summary>Gets the value transformer configurations.</summary>
        public IEnumerable<ValueTransformerConfiguration> ValueTransformers => Profile.ValueTransformers;

        /// <summary>
        /// Whether null destination values are allowed for the given member map.
        /// </summary>
        public bool AllowsNullDestinationValuesFor(MemberMap memberMap)
        {
            if (memberMap != null && memberMap.AllowNull.HasValue)
                return memberMap.AllowNull.Value;
            return AllowNullDestinationValues;
        }

        /// <summary>
        /// Whether null collections are allowed for the given member map (member override first).
        /// </summary>
        public bool AllowsNullCollectionsFor(MemberMap memberMap)
        {
            if (memberMap != null && memberMap.AllowNull.HasValue)
                return memberMap.AllowNull.Value;
            return AllowNullCollections;
        }
    }

    /// <summary>
    /// A type map configuration (base for mapping expressions).
    /// </summary>
    public abstract class TypeMapConfiguration
    {
        /// <summary>Gets the source type.</summary>
        public Type SourceType { get; internal set; }

        /// <summary>Gets the destination type.</summary>
        public Type DestinationType { get; internal set; }

        /// <summary>Gets or sets the type map.</summary>
        public TypeMap TypeMap { get; set; }

        /// <summary>Gets the type pair.</summary>
        public TypePair Types => new TypePair(SourceType, DestinationType);

        /// <summary>Gets or sets whether a type converter is used.</summary>
        public bool HasTypeConverter { get; set; }

        /// <summary>Gets or sets whether this is a reverse map.</summary>
        public bool IsReverseMap { get; set; }
    }

    /// <summary>
    /// Member configuration (naming conventions and name mappers).
    /// </summary>
    public class MemberConfiguration
    {
        /// <summary>Gets or sets the source naming convention.</summary>
        public INamingConvention SourceNamingConvention { get; set; }

        /// <summary>Gets or sets the destination naming convention.</summary>
        public INamingConvention DestinationNamingConvention { get; set; }
    }
}
