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

            var revitDs = Path.Combine(dir, "DynamoRevitDS.dll");
            var core = Path.Combine(dir, "DynamoCore.dll");
            if (!File.Exists(revitDs) || !File.Exists(core))
                return caps; // IsPresent stays false

            caps.IsPresent = true;
            try { caps.DynamoVersion = FileVersionInfo.GetVersionInfo(core).FileVersion ?? ""; }
            catch { caps.DynamoVersion = ""; }

            // Revit 2024+ ships Dynamo where CPython3 is available/standard.
            caps.CPython3Expected = revitYear >= 2024;
            return caps;
        }
    }
}
