using System.Diagnostics;
using System.IO;

namespace RevitCortex.Tools.Dynamo.Runtime
{
    public sealed class DynamoCapabilities
    {
        public bool IsPresent { get; internal set; }
        public string DynamoVersion { get; internal set; } = "";
        public bool CPython3Expected { get; internal set; }
        public string DynamoForRevitDir { get; internal set; } = "";
    }

    /// <summary>
    /// Detects presence/version of Dynamo for Revit by file inspection only
    /// (FileVersionInfo, no Assembly.Load). Safe to run at document open.
    /// </summary>
    public sealed class DynamoCapabilityProbe
    {
        private readonly string _autodeskBase;

        public DynamoCapabilityProbe(string? autodeskBase = null)
        {
            _autodeskBase = string.IsNullOrEmpty(autodeskBase)
                ? DynamoPaths.ProgramFilesAutodesk()
                : autodeskBase!;
        }

        public DynamoCapabilities Probe(int revitYear)
        {
            var caps = new DynamoCapabilities();
            var dir = DynamoPaths.DynamoForRevitDir(_autodeskBase, revitYear);
            caps.DynamoForRevitDir = dir;

            // Real installs place the DLLs under {dir}\Revit\; older/other layouts keep
            // them at the root. Mirror DynamoRuntimeLoader: probe the Revit subfolder first,
            // then the root. IsPresent requires BOTH dlls to be found.
            if (!TryResolveDll(dir, "DynamoRevitDS.dll", out _) ||
                !TryResolveDll(dir, "DynamoCore.dll", out var core))
                return caps; // IsPresent stays false

            caps.IsPresent = true;
            try { caps.DynamoVersion = FileVersionInfo.GetVersionInfo(core).FileVersion ?? ""; }
            catch { caps.DynamoVersion = ""; }

            // Revit 2024+ ships Dynamo where CPython3 is available/standard.
            caps.CPython3Expected = revitYear >= 2024;
            return caps;
        }

        /// <summary>
        /// Looks for <paramref name="fileName"/> in {dir}\Revit\ first, then {dir}\ (root).
        /// Returns the full path of whichever exists via <paramref name="path"/>.
        /// </summary>
        private static bool TryResolveDll(string dir, string fileName, out string path)
        {
            var revitSub = Path.Combine(dir, "Revit", fileName);
            if (File.Exists(revitSub)) { path = revitSub; return true; }

            var root = Path.Combine(dir, fileName);
            if (File.Exists(root)) { path = root; return true; }

            path = "";
            return false;
        }
    }
}
