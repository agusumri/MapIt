using System.Collections.Generic;
using System.Linq;

namespace MapIt
{
    /// <summary>
    /// A single member change recorded by diff/patch mapping (<c>MapChanges</c>): the destination
    /// member name, its previous value, and the new value.
    /// </summary>
    public sealed class MemberChange
    {
        /// <summary>Creates a member change record.</summary>
        public MemberChange(string memberName, object oldValue, object newValue)
        {
            MemberName = memberName;
            OldValue = oldValue;
            NewValue = newValue;
        }

        /// <summary>The destination member name that changed.</summary>
        public string MemberName { get; }

        /// <summary>The value before mapping.</summary>
        public object OldValue { get; }

        /// <summary>The value after mapping.</summary>
        public object NewValue { get; }

        /// <summary>Returns a readable "Member: old -> new" summary (no sensitive formatting).</summary>
        public override string ToString() => $"{MemberName}: {OldValue ?? "null"} -> {NewValue ?? "null"}";
    }

    /// <summary>
    /// The result of a diff/patch mapping: the patched destination and the list of changed members.
    /// </summary>
    public sealed class MapChangeResult<TDestination>
    {
        internal MapChangeResult(TDestination destination, IReadOnlyList<MemberChange> changes)
        {
            Destination = destination;
            Changes = changes;
        }

        /// <summary>The destination instance after patching (same instance that was passed in).</summary>
        public TDestination Destination { get; }

        /// <summary>The members that changed, in the order they were applied.</summary>
        public IReadOnlyList<MemberChange> Changes { get; }

        /// <summary>Whether any member changed.</summary>
        public bool HasChanges => Changes.Count > 0;

        /// <summary>The names of the changed members.</summary>
        public IEnumerable<string> ChangedMemberNames => Changes.Select(c => c.MemberName);
    }
}
