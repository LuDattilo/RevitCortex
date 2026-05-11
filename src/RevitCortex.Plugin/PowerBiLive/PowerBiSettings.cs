using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Persistent configuration for the Power BI Live integration. Lives at
/// <c>~/.revitcortex/powerbi-live.json</c>. Auth tokens DO NOT live here —
/// MSAL keeps them in its own DPAPI-protected cache.
/// </summary>
public class PowerBiSettings
{
    /// <summary>
    /// Azure AD application (client) id used by MSAL. Default = the Microsoft
    /// "Power BI Embedded Sample" public client (well-known, works on any
    /// tenant without admin setup). Tenants with strict policies may require
    /// overriding this with a dedicated app registration.
    /// </summary>
    public string ClientId { get; set; } = "871c010f-5e61-4fb1-83ac-98610a7e9110";

    /// <summary>
    /// Azure AD tenant id ("common" lets any organization sign in). Most
    /// users should leave this as "organizations" so personal Microsoft
    /// accounts are excluded but every work tenant is allowed.
    /// </summary>
    public string TenantId { get; set; } = "organizations";

    /// <summary>
    /// Default workspace the publisher targets. Empty until the user picks one.
    /// </summary>
    public string? DefaultWorkspaceId { get; set; }

    /// <summary>
    /// Default dataset within the chosen workspace.
    /// </summary>
    public string? DefaultDatasetId { get; set; }

    /// <summary>
    /// Default report id used by <c>open_in_powerbi</c> for URL filter.
    /// </summary>
    public string? DefaultReportId { get; set; }

    /// <summary>
    /// If true, push tools are blocked unless ReadOnlyMode also allows external writes.
    /// Defaults to false: behave like classic RevitCortex read-only — block PBI pushes too.
    /// </summary>
    public bool AllowExternalWrites { get; set; }

    /// <summary>
    /// Selection-watch debounce window in milliseconds. Outside this range
    /// the publisher will warn (too low → throttling, too high → laggy).
    /// </summary>
    public int SelectionDebounceMs { get; set; } = 1000;

    /// <summary>
    /// Per-document workspace/dataset bindings. Key is a stable document key
    /// computed by <see cref="ProjectDocumentKey.Compute"/>.
    /// </summary>
    public Dictionary<string, ProjectBinding> ProjectBindings { get; set; }
        = new Dictionary<string, ProjectBinding>();

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "powerbi-live.json");

    public static PowerBiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new PowerBiSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonConvert.DeserializeObject<PowerBiSettings>(json) ?? new PowerBiSettings();
        }
        catch
        {
            return new PowerBiSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath,
            JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    /// <summary>
    /// Returns the binding for the given document key, or null if not found.
    /// </summary>
    public ProjectBinding? GetBinding(string documentKey)
    {
        if (ProjectBindings.TryGetValue(documentKey, out var b)) return b;
        return null;
    }

    /// <summary>
    /// Saves or updates a binding for the given document key.
    /// </summary>
    public void SetBinding(string documentKey, ProjectBinding binding)
    {
        binding.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        ProjectBindings[documentKey] = binding;
        Save();
    }
}

/// <summary>
/// Workspace + dataset binding for a specific Revit document.
/// Stored in powerbi-live.json under ProjectBindings[documentKey].
/// </summary>
public class ProjectBinding
{
    public string WorkspaceId { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string DatasetName { get; set; } = "";
    /// <summary>Display label only — not used as key.</summary>
    public string ProjectName { get; set; } = "";
    /// <summary>Revit ProjectInformation.UniqueId if available.</summary>
    public string DocumentGuid { get; set; } = "";
    /// <summary>SHA256 of the full local/central path (fallback key).</summary>
    public string LastPathHash { get; set; } = "";
    public string SchemaVersion { get; set; } = "1.0";
    public string UpdatedAtUtc { get; set; } = "";
}

/// <summary>
/// Computes a stable, collision-resistant key for a Revit document
/// used as the ProjectBindings dictionary key.
///
/// Priority:
///   1. Cloud model GUID (BIM360/ACC) — most stable.
///   2. ProjectInformation.UniqueId — stable across Save As on local files.
///   3. SHA256(normalized full path) — fallback for files without project info.
///
/// ProjectName is intentionally excluded from the key: it can be renamed
/// without creating a new dataset.
/// </summary>
public static class ProjectDocumentKey
{
    public static string Compute(Document doc)
    {
        // Priority 1: cloud model GUID
        try
        {
            var cloud = doc.GetCloudModelPath();
            if (cloud != null)
            {
                var str = cloud.ToString();
                if (!string.IsNullOrWhiteSpace(str))
                    return "cloud:" + str;
            }
        }
        catch { }

        // Priority 2: ProjectInformation.UniqueId
        try
        {
            var uid = doc.ProjectInformation?.UniqueId;
            if (!string.IsNullOrWhiteSpace(uid))
                return "projuid:" + uid;
        }
        catch { }

        // Priority 3: SHA256 of normalized path
        try
        {
            var path = doc.PathName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var normalized = path.ToUpperInvariant().Replace('\\', '/').Trim();
                var hash = ComputeSha256(normalized);
                return "pathhash:" + hash;
            }
        }
        catch { }

        // Last resort: random (won't persist across sessions, but won't crash)
        return "tmp:" + Guid.NewGuid().ToString("N");
    }

    public static string ComputePathHash(string path)
    {
        var normalized = path.ToUpperInvariant().Replace('\\', '/').Trim();
        return ComputeSha256(normalized);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.AppendFormat("{0:x2}", b);
        return sb.ToString().Substring(0, 16); // 16 hex chars — enough for collision resistance
    }
}
