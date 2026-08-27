using System;
using System.IO;

namespace RiveTT.Core.Hosting;

/// <summary>
/// Local paths used by the automatic Revit integration (2026.5+ or 2027).
/// </summary>
public class CortexEnvironment
{
    public string RootFolder { get; }

    public string AuditLogPath => Path.Combine(RootFolder, "audit.jsonl");
    public string ScriptsFolder => Path.Combine(RootFolder, "scripts");

    private CortexEnvironment(string rootFolder)
    {
        RootFolder = rootFolder;
    }

    private static CortexEnvironment? _current;

    /// <summary>Process-wide storage location.</summary>
    public static CortexEnvironment Current
    {
        get
        {
            var c = _current;
            if (c == null)
            {
                c = CreateDefault();
                _current = c;
            }
            return c;
        }
    }

    /// <summary>Test seam: force a storage location (pass null to re-detect).</summary>
    public static void OverrideForTests(CortexEnvironment? env) { _current = env; }

    public static CortexEnvironment CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiveTT"));

    /// <summary>Test-only storage location that never touches user data.</summary>
    public static CortexEnvironment ForTests(string rootFolder) =>
        new(rootFolder);

}
