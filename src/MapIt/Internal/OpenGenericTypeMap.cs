using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Carries the configuration recorded against an open-generic map definition so it can be
    /// replayed against a closed <see cref="TypeMap"/> on demand. (Phase 2 fills in the replay.)
    /// </summary>
    internal sealed class OpenGenericTypeMap
    {
        public OpenGenericTypeMap(Type sourceDefinition, Type destinationDefinition)
        {
            SourceDefinition = sourceDefinition;
            DestinationDefinition = destinationDefinition;
        }

        public Type SourceDefinition { get; }
        public Type DestinationDefinition { get; }

        /// <summary>The ordered configuration steps recorded against the open map.</summary>
        public List<OpenMapConfigStep> Steps { get; } = new List<OpenMapConfigStep>();

        /// <summary>Included derived open pairs (Include) to close alongside this map.</summary>
        public List<(Type Source, Type Destination)> IncludedDerived { get; } = new List<(Type, Type)>();

        /// <summary>Included base open pairs (IncludeBase).</summary>
        public List<(Type Source, Type Destination)> IncludedBase { get; } = new List<(Type, Type)>();

        /// <summary>Open destination-type override (As with an open definition).</summary>
        public Type DestinationTypeOverrideDefinition { get; set; }

        /// <summary>Whether a reverse open map should also be registered.</summary>
        public bool ReverseMap { get; set; }

        public int? MaxDepth { get; set; }
        public bool PreserveReferences { get; set; }
        public bool IncludeAllDerived { get; set; }
    }

    /// <summary>Base class for a recorded open-generic configuration step (replayed at close time).</summary>
    internal abstract class OpenMapConfigStep
    {
    }

    /// <summary>A recorded value recipe: ignore, a source member-name chain, or a raw lambda / resolver type.</summary>
    internal sealed class OpenValueRecipe
    {
        public bool Ignore { get; set; }
        public string[] SourceMemberNames { get; set; }
        public LambdaExpression RawExpression { get; set; }
        public Type ResolverType { get; set; }
        public Type ConverterType { get; set; }
        public object NullSubstitute { get; set; }
        public bool? AllowNull { get; set; }
        public bool ExplicitExpansion { get; set; }
        public bool? UseDestinationValue { get; set; }
        public int? MappingOrder { get; set; }
    }

    /// <summary>Recorded ForMember against an open map (destination member by name).</summary>
    internal sealed class OpenMemberStep : OpenMapConfigStep
    {
        public string DestinationMemberName { get; set; }
        public OpenValueRecipe Recipe { get; set; }
    }

    /// <summary>Recorded ForPath against an open map (destination path by segment names).</summary>
    internal sealed class OpenPathStep : OpenMapConfigStep
    {
        public string[] PathSegmentNames { get; set; }
        public OpenValueRecipe Recipe { get; set; }
    }

    /// <summary>Recorded ForCtorParam against an open map (parameter by name).</summary>
    internal sealed class OpenCtorParamStep : OpenMapConfigStep
    {
        public string CtorParamName { get; set; }
        public OpenValueRecipe Recipe { get; set; }
    }
}
