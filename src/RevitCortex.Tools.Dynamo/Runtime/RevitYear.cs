namespace RevitCortex.Tools.Dynamo.Runtime
{
    /// <summary>The Revit version this build targets, from compile-time REVIT20xx_OR_GREATER constants.</summary>
    public static class RevitYear
    {
        public static int Current =>
#if REVIT2027_OR_GREATER
            2027;
#elif REVIT2026_OR_GREATER
            2026;
#elif REVIT2025_OR_GREATER
            2025;
#elif REVIT2024_OR_GREATER
            2024;
#else
            2023;
#endif
    }
}
