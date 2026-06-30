using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Security;

/// <summary>
/// User-editable settings persisted at ~/.revitcortex/settings.json.
/// Missing file or parse errors return defaults (all opt-in features disabled).
/// </summary>
public class CortexSettings
{
    /// <summary>
    /// When false (default), send_code_to_revit is refused at the tool-invocation boundary.
    /// The user must explicitly enable dynamic code execution via settings.json or the
    /// Revit plugin Settings UI. This is a hard gate, not a soft warning.
    /// </summary>
    [JsonProperty("EnableCodeExecution")]
    public bool EnableCodeExecution { get; set; } = false;

    /// <summary>TCP port for plugin-to-server communication.</summary>
    [JsonProperty("Port")]
    public int Port { get; set; } = 8080;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public static CortexSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return new CortexSettings();
            var json = File.ReadAllText(file);
            return JsonConvert.DeserializeObject<CortexSettings>(json) ?? new CortexSettings();
        }
        catch
        {
            return new CortexSettings();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    /// <summary>
    /// Merge-writes ONLY the EnableCodeExecution flag into the settings file,
    /// preserving every other key (e.g. DisabledTools, Port) that this strongly-typed
    /// class does not model. Use this instead of <see cref="Save"/> from the settings UI,
    /// where a full re-serialize would silently drop keys owned by other pages.
    /// </summary>
    public static void SetEnableCodeExecution(bool enabled, string? path = null)
    {
        var file = path ?? DefaultPath;
        JObject root;
        try
        {
            root = File.Exists(file) ? JObject.Parse(File.ReadAllText(file)) : new JObject();
        }
        catch
        {
            root = new JObject();
        }

        root["EnableCodeExecution"] = enabled;

        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(file, root.ToString(Formatting.Indented));
    }
}
