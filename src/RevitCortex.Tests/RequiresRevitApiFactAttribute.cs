using System;
using Xunit;

namespace RevitCortex.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that auto-skips when the Revit UI API assembly
/// (RevitAPIUI) cannot be loaded. The Nice3point.Revit.Api.* NuGet packages are
/// reference-only assemblies: they exist at compile time but are not copied to
/// the test output and are not present unless Revit itself is installed. Tests
/// that exercise types implementing IExternalEventHandler / taking UIApplication
/// trigger a runtime load of RevitAPIUI and would otherwise fail with a
/// FileNotFoundException on machines (and CI) without Revit.
///
/// Marking such a test with [RequiresRevitApiFact] turns that environmental
/// failure into an honest Skip, keeping the suite green where Revit is absent
/// while still running the test where Revit (and thus RevitAPIUI) is available.
/// </summary>
public sealed class RequiresRevitApiFactAttribute : FactAttribute
{
    public RequiresRevitApiFactAttribute()
    {
        if (!RevitApiAvailability.IsRevitUiApiLoadable)
            Skip = "Requires RevitAPIUI (reference-only NuGet assembly; present only with a Revit install).";
    }
}

internal static class RevitApiAvailability
{
    private static readonly Lazy<bool> _loadable = new Lazy<bool>(Probe);

    public static bool IsRevitUiApiLoadable => _loadable.Value;

    private static bool Probe()
    {
        try
        {
            // typeof() alone may resolve from reference metadata without pulling the
            // physical assembly off disk, so it can falsely report "loadable". Force an
            // actual load: a method body that touches a member of a RevitAPIUI type makes
            // the JIT resolve and load the real assembly, throwing if the file is absent.
            return ForceLoad();
        }
        catch
        {
            return false;
        }
    }

    // Separate method so the JIT only resolves RevitAPIUI when this is actually invoked
    // (inside the try), not when Probe is JITted.
    private static bool ForceLoad()
    {
        System.Reflection.Assembly.Load("RevitAPIUI");
        return true;
    }
}
