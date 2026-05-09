using System;
using System.IO;
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
}
