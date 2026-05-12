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

    // Windows device names that cannot be used as file names on any path.
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static void Save(PowerBiExportProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name cannot be empty.", nameof(profile));

        Directory.CreateDirectory(ProfilesDir);
        profile.LastUsed = DateTime.UtcNow;
        var json = JsonConvert.SerializeObject(profile, JsonSettings);

        // Atomic write: write to .tmp then rename, consistent with WriteCsvAtomic used
        // elsewhere in the project. Prevents a corrupt profile file on Revit crash.
        var finalPath = ProfilePath(profile.Name);
        var tmpPath = finalPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tmpPath, finalPath);
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
        var safe = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        // Prevent Windows reserved device names (NUL, CON, COM1 …) which silently
        // discard writes or throw unpredictable exceptions.
        if (string.IsNullOrEmpty(safe) || WindowsReservedNames.Contains(safe))
            safe = "_" + safe;
        return safe;
    }
}
