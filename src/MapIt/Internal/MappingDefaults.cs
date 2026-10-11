namespace MapIt
{
    /// <summary>
    /// Centralized default values for the mapping engine's safety limits (BEHAVIOR S1/S5).
    /// This is the single source of truth for these numeric defaults; no other literal for
    /// these options should appear in the source.
    /// </summary>
    internal static class MappingDefaults
    {
        /// <summary>Maximum nested mapping depth before the engine throws (BEHAVIOR S1).</summary>
        public const int MaxGraphDepth = 256;

        /// <summary>Maximum number of items enumerated from a single source collection (BEHAVIOR S5).</summary>
        public const int MaxCollectionItems = 1_000_000;
    }
}
