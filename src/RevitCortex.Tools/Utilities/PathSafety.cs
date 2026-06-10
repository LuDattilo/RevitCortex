using System;
using System.Collections.Generic;
using System.IO;

namespace RevitCortex.Tools.Utilities;

/// <summary>
/// Validates user-supplied file paths before read/write, restricting them to a
/// small allowlist of user-owned directories. Prevents path-traversal and
/// arbitrary-file access from MCP callers (ultrareview H25/H28/H36).
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Canonical, trailing-separator-terminated roots a caller-supplied path is
    /// allowed to resolve under. Computed once; SpecialFolder lookups are cheap
    /// but stable for the process lifetime.
    /// </summary>
    private static readonly Lazy<string[]> AllowedRoots = new(BuildAllowedRoots);

    private static string[] BuildAllowedRoots()
    {
        var roots = new List<string>();

        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(p)) roots.Add(WithSeparator(Path.GetFullPath(p)));
            }
            catch { /* folder unavailable on this OS/profile */ }
        }

        Add(Environment.SpecialFolder.MyDocuments);
        Add(Environment.SpecialFolder.DesktopDirectory);
        Add(Environment.SpecialFolder.UserProfile);

        // Downloads has no SpecialFolder enum value; derive from the profile.
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
                roots.Add(WithSeparator(Path.GetFullPath(Path.Combine(profile, "Downloads"))));
        }
        catch { }

        try { roots.Add(WithSeparator(Path.GetFullPath(Path.GetTempPath()))); }
        catch { }

        return roots.ToArray();
    }

    private static string WithSeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            path += Path.DirectorySeparatorChar;
        return path;
    }

    /// <summary>
    /// Resolves <paramref name="userPath"/> to an absolute canonical path and verifies
    /// it lives under one of the allowed user directories. Returns false (with a reason)
    /// for traversal, UNC, or system-directory paths.
    /// </summary>
    /// <param name="userPath">The raw caller-supplied path.</param>
    /// <param name="resolvedPath">The canonical absolute path, when valid.</param>
    /// <param name="error">A human-readable reason when invalid.</param>
    /// <param name="allowUnc">Accept UNC/network paths (\\host\share). Reserved for link
    /// tools, where loading models from network shares is a standard BIM workflow and
    /// every call is already gated by a user confirmation dialog showing the path.
    /// Local paths stay restricted to the allowed user directories regardless.</param>
    public static bool TryResolveSafe(string? userPath, out string resolvedPath, out string error, bool allowUnc = false)
    {
        resolvedPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(userPath))
        {
            error = "No file path provided.";
            return false;
        }

        string full;
        try
        {
            // GetFullPath collapses ".." and normalizes separators.
            full = Path.GetFullPath(userPath);
        }
        catch (Exception ex)
        {
            error = $"Invalid path: {ex.Message}";
            return false;
        }

        // UNC / network shares (\\host\share or //host/share): rejected by default;
        // link tools opt in because shares can never live under the user-profile roots.
        if (full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith("//", StringComparison.Ordinal))
        {
            if (allowUnc)
            {
                resolvedPath = full;
                return true;
            }
            error = "Network/UNC paths are not allowed.";
            return false;
        }

        foreach (var root in AllowedRoots.Value)
        {
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                resolvedPath = full;
                return true;
            }
        }

        error = "Path is outside the allowed directories " +
                "(Documents, Desktop, Downloads, user profile, or temp).";
        return false;
    }
}
