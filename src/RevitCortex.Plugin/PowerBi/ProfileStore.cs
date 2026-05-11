using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Reads/writes Power BI export profiles as individual JSON files in
/// ~/.revitcortex/profiles/. One file per profile keeps things simple
/// and lets the user copy profiles between machines.
/// </summary>
public static class ProfileStore
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex",
        "profiles");

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string GetProfilesDirectory() => ProfilesDir;

    public static List<PowerBiExportProfile> LoadAll()
    {
        var result = new List<PowerBiExportProfile>();
        if (!Directory.Exists(ProfilesDir)) return result;

        foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonConvert.DeserializeObject<PowerBiExportProfile>(json);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    result.Add(profile);
            }
            catch
            {
                // Skip corrupted profiles silently — UI can still operate
            }
        }
        return result.OrderByDescending(p => p.LastUsed).ToList();
    }

    public static PowerBiExportProfile? Load(string name)
    {
        var path = ProfilePath(name);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonConvert.DeserializeObject<PowerBiExportProfile>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(PowerBiExportProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name cannot be empty.", nameof(profile));

        Directory.CreateDirectory(ProfilesDir);
        profile.LastUsed = DateTime.UtcNow;
        var json = JsonConvert.SerializeObject(profile, JsonSettings);
        File.WriteAllText(ProfilePath(profile.Name), json);
    }

    public static bool Delete(string name)
    {
        var path = ProfilePath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string ProfilePath(string name)
    {
        var safe = SanitizeFileName(name);
        return Path.Combine(ProfilesDir, safe + ".json");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }
}
