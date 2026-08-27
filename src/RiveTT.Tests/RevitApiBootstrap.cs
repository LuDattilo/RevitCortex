using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace RiveTT.Tests;

/// <summary>
/// Best-effort local test enabler, not a build requirement. Nice3point.Revit.Api.* ships
/// a compile-only reference assembly — no real RevitAPI.dll/RevitAPIUI.dll anywhere in the
/// package. If a real Revit install is found on this machine, redirect the runtime's
/// assembly resolution to load the actual DLLs FROM Revit's own install directory (never
/// copied — they are proprietary and machine-specific), and add that directory to the
/// native DLL search path so the managed/native dependencies inside those DLLs resolve the
/// way they do when Revit itself loads them.
///
/// If no local Revit install is found (any other dev machine, GitHub Actions, CI), this is
/// a silent no-op: resolution falls through to the existing behavior, and
/// RequiresRevitApiFactAttribute's probe fails as it already does, marking the test Skip —
/// never Fail. Nothing here can make a test suite red for lack of Revit.
/// </summary>
internal static class RevitApiBootstrap
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var installDir = FindRevitInstallDir();
        if (installDir == null)
            return;

        // Lets the native side of RevitAPI.dll/RevitAPIUI.dll (and whatever they pull in —
        // AdWindows.dll, UIFramework.dll, and more not enumerated here) resolve from the
        // real install directory, the same way Revit.exe's own process does it.
        NativeMethods.SetDllDirectory(installDir);

        // Not just RevitAPI/RevitAPIUI: their C++/CLI <Module> initializers pull in more
        // managed assemblies transitively (RevitAPIFoundation, seen empirically, and
        // whatever comes after it). Resolving only fires once normal probing has already
        // failed, so falling back to "does Revit's own install dir have a same-named DLL"
        // cannot hijack resolution of anything that isn't already unresolved.
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == null)
                return null;

            var path = Path.Combine(installDir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    /// <summary>
    /// REVIT_INSTALL_DIR overrides everything else — set it when Revit lives somewhere
    /// other than the two default locations, or to force this off by pointing it at a
    /// directory with no RevitAPI.dll.
    /// </summary>
    private static string? FindRevitInstallDir()
    {
        // An explicit override always wins, in both directions: a valid directory is used
        // as-is, and an invalid one intentionally disables detection instead of falling
        // through to the defaults below — the only way to force this off on a machine that
        // does have Revit installed at one of the default locations.
        var overridden = Environment.GetEnvironmentVariable("REVIT_INSTALL_DIR");
        if (!string.IsNullOrEmpty(overridden))
            return HasRevitApi(overridden) ? overridden : null;

        foreach (var candidate in KnownInstallDirs)
        {
            if (HasRevitApi(candidate))
                return candidate;
        }

        return null;
    }

    private static readonly string[] KnownInstallDirs =
    {
        @"C:\Program Files\Autodesk\Revit 2027",
        @"C:\Program Files\Autodesk\Revit 2026",
    };

    private static bool HasRevitApi(string dir) => File.Exists(Path.Combine(dir, "RevitAPI.dll"));

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetDllDirectory(string? lpPathName);
    }
}
