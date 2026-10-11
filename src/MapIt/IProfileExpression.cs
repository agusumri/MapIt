using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// A list of members to validate during configuration.
    /// </summary>
    public enum MemberList
    {
        /// <summary>Validate destination members (default).</summary>
        Destination = 0,
        /// <summary>Validate source members.</summary>
        Source = 1,
        /// <summary>Do not validate members.</summary>
        None = 2
    }

    /// <summary>
    /// A naming convention for matching source to destination member names.
    /// </summary>
    public interface INamingConvention
    {
        /// <summary>Splits the input into words using this convention.</summary>
        string[] Split(string input);

        /// <summary>Gets the separator character used by this convention.</summary>
        string SeparatorCharacter { get; }
    }
}
