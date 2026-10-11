using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A mapping configuration for a single member (property, path, or ctor parameter).
    /// </summary>
    public class MemberMap
    {
        /// <summary>Gets the destination name.</summary>
        public string DestinationName { get; internal set; }

        /// <summary>Gets or sets the destination type.</summary>
        public Type DestinationType { get; set; }

        /// <summary>Gets or sets the source member (first in chain).</summary>
        public MemberInfo SourceMember { get; internal set; }

        /// <summary>Gets or sets the full source member chain.</summary>
        public MemberInfo[] SourceMembers { get; set; }

        /// <summary>Gets the source type.</summary>
        public Type SourceType { get; internal set; }

        /// <summary>Gets or sets the type map this member belongs to.</summary>
        public TypeMap TypeMap { get; set; }

        /// <summary>Gets or sets the profile.</summary>
        public ProfileMap Profile { get; internal set; }

        /// <summary>Gets or sets whether the member is ignored.</summary>
        public bool Ignored { get; set; }

        /// <summary>Gets or sets the resolver used to resolve the member.</summary>
        public IValueResolverMember Resolver { get; set; }

        /// <summary>Gets or sets the custom mapping expression (lambda).</summary>
        public Expression CustomMapExpression { get; internal set; }

        /// <summary>Gets or sets the condition expression.</summary>
        public LambdaExpression Condition { get; set; }

        /// <summary>Gets or sets the precondition expression.</summary>
        public LambdaExpression PreCondition { get; set; }

        /// <summary>Internal typed condition delegate.</summary>
        internal Func<object, object, object, object, ResolutionContext, bool> ConditionDelegate { get; set; }

        /// <summary>Internal typed precondition delegate.</summary>
        internal Func<object, object, object, object, ResolutionContext, bool> PreConditionDelegate { get; set; }

        /// <summary>Gets or sets the null substitute value.</summary>
        public object NullSubstitute { get; set; }

        /// <summary>Gets or sets whether the member allows null.</summary>
        public bool? AllowNull { get; set; }

        /// <summary>
        /// True when this member map was created or touched by an explicit user configuration
        /// (ForMember/ForPath/attributes) rather than by convention discovery. Used by
        /// <see cref="TypeMap.HasConfiguredMembersOrCtorOrConverter"/> to tell them apart.
        /// </summary>
        internal bool IsExplicitlyConfigured { get; set; }

        /// <summary>Recorded [MapAtRuntime] flag (accepted and ignored at runtime per §11).</summary>
        internal bool MapAtRuntime { get; set; }

        /// <summary>
        /// Optional async resolver producing a Task&lt;object&gt; for this member, awaited by MapAsync.
        /// Signature: (source, destination, destMember, context, cancellationToken) -> Task&lt;object&gt;.
        /// </summary>
        internal System.Func<object, object, object, ResolutionContext, System.Threading.CancellationToken, System.Threading.Tasks.Task<object>> AsyncResolver { get; set; }

        /// <summary>Whether the member is explicitly expanded in projections.</summary>
        internal bool IsExplicitExpansion { get; set; }

        /// <summary>Gets or sets whether the destination value should be used.</summary>
        public bool? UseDestinationValue { get; set; }

        /// <summary>Gets or sets whether the member is explicitly expanded.</summary>
        public bool? ExplicitExpansion { get; set; }

        /// <summary>Gets or sets whether the member can be set.</summary>
        public virtual bool CanBeSet { get; internal set; } = true;

        /// <summary>Gets whether the member can resolve a value.</summary>
        public bool CanResolveValue => SourceMembers != null || Resolver != null;

        /// <summary>Gets whether the member is mapped.</summary>
        public bool IsMapped => !Ignored && (SourceMembers != null || Resolver != null || CanBeSet);

        /// <summary>Gets whether a resolve is configured.</summary>
        public bool IsResolveConfigured => Resolver != null || CustomMapExpression != null;

        /// <summary>Gets or sets the value transformers.</summary>
        public IReadOnlyCollection<ValueTransformerConfiguration> ValueTransformers { get; internal set; } = Array.Empty<ValueTransformerConfiguration>();

        /// <summary>Gets the types this member maps between.</summary>
        public TypePair Types() => new TypePair(SourceType, DestinationType);

        /// <summary>Maps by convention from the given source members.</summary>
        public void MapByConvention(MemberInfo[] sourceMembers) => SourceMembers = sourceMembers;

        /// <summary>Sets the resolver.</summary>
        public void SetResolver(IValueResolverMember resolver) => Resolver = resolver;

        /// <summary>Gets the source member name.</summary>
        public string GetSourceMemberName() => SourceMembers != null && SourceMembers.Length > 0
            ? string.Join(".", SourceMembers.Select(m => m.Name))
            : DestinationName;

        public override string ToString() => $"{DestinationName} = {GetSourceMemberName()}";
    }

    /// <summary>
    /// A property mapping (destination property).
    /// </summary>
    public class PropertyMap : MemberMap
    {
        /// <summary>Gets the destination member info.</summary>
        public MemberInfo DestinationMember { get; internal set; }

        /// <summary>Gets or sets the mapping order.</summary>
        public int? MappingOrder { get; set; }

        /// <summary>Adds a value transformation.</summary>
        public void AddValueTransformation(ValueTransformerConfiguration config)
        {
            var list = new List<ValueTransformerConfiguration>(ValueTransformers) { config };
            ValueTransformers = list;
        }

        /// <summary>Applies an inherited property map.</summary>
        public void ApplyInheritedPropertyMap(PropertyMap inheritedMappedProperty) { }
    }

    /// <summary>
    /// A path mapping (nested destination path).
    /// </summary>
    public class PathMap : MemberMap
    {
        /// <summary>Gets the member path.</summary>
        public MemberPath MemberPath { get; internal set; }

        /// <summary>Gets the destination expression.</summary>
        public LambdaExpression DestinationExpression { get; internal set; }

        /// <summary>Gets whether the path can be set.</summary>
        public new bool CanBeSet => true;
    }

    /// <summary>
    /// A source member configuration.
    /// </summary>
    public class SourceMemberConfig
    {
        /// <summary>Gets or sets the source member.</summary>
        public MemberInfo SourceMember { get; internal set; }

        private bool _ignored;

        /// <summary>Ignores this source member.</summary>
        public void Ignore() => _ignored = true;

        /// <summary>Whether the source member is ignored.</summary>
        public bool IsIgnored() => _ignored;
    }

    /// <summary>
    /// A constructor parameter mapping.
    /// </summary>
    public class ConstructorParameterMap : MemberMap
    {
        /// <summary>Gets the parameter info.</summary>
        public ParameterInfo Parameter { get; internal set; }

        /// <summary>Gets or sets the explicit expansion setting.</summary>
        public new bool? ExplicitExpansion { get; set; }
    }

    /// <summary>
    /// A chain of member info representing a member path.
    /// </summary>
    public struct MemberPath : IEquatable<MemberPath>
    {
        /// <summary>Creates a member path.</summary>
        public MemberPath(params MemberInfo[] members) => Members = members;

        /// <summary>Gets the member chain.</summary>
        public MemberInfo[] Members { get; set; }

        /// <summary>Gets the first member.</summary>
        public MemberInfo First => Members?.FirstOrDefault();

        /// <summary>Gets the last member.</summary>
        public MemberInfo Last => Members?.LastOrDefault();

        /// <summary>Gets the path length.</summary>
        public int Length => Members?.Length ?? 0;

        /// <summary>Concats another path.</summary>
        public MemberPath Concat(IEnumerable<MemberInfo> memberInfos)
        {
            var all = new List<MemberInfo>(Members ?? Array.Empty<MemberInfo>());
            all.AddRange(memberInfos);
            return new MemberPath(all.ToArray());
        }

        /// <summary>Whether this path starts with another.</summary>
        public bool StartsWith(MemberPath path)
        {
            if (path.Length == 0 || Length < path.Length) return false;
            for (var i = 0; i < path.Length; i++)
                if (!Equals(Members[i], path.Members[i])) return false;
            return true;
        }

        public void Deconstruct(out MemberInfo[] Members) => Members = this.Members;

        public bool Equals(MemberPath other) =>
            Members != null && other.Members != null && Members.SequenceEqual(other.Members);

        public override bool Equals(object obj) => obj is MemberPath other && Equals(other);

        public override int GetHashCode() => Members == null ? 0 : string.Join(".", Members.Select(m => m.Name)).GetHashCode();

        public override string ToString() => Members == null ? string.Empty : string.Join(".", Members.Select(m => m.Name));
    }
}
