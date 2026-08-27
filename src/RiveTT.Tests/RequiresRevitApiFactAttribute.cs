using System;
using Xunit;

namespace RiveTT.Tests;

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
///
/// RevitAPIUI stays unloadable even with RevitApiBootstrap and a real Revit
/// install present (its native init needs more than a sibling-directory probe
/// can supply — measured 27/08/2026) — use this attribute, not
/// <see cref="RequiresRevitDbApiFactAttribute"/>, for anything that touches
/// UIApplication/UIDocument or activates a document in the Revit interface.
/// </summary>
public sealed class RequiresRevitApiFactAttribute : FactAttribute
{
    public RequiresRevitApiFactAttribute()
    {
        if (!RevitApiAvailability.IsRevitUiApiLoadable)
            Skip = "Requires RevitAPIUI (reference-only NuGet assembly; present only with a Revit install).";
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that auto-skips when the base Revit API assembly
/// (RevitAPI — Document, Application, ElementId, ...) cannot be loaded. Narrower
/// than <see cref="RequiresRevitApiFactAttribute"/>: RevitApiBootstrap makes
/// RevitAPI loadable standalone when a local Revit install is found (its native
/// dependency chain resolves through a same-directory fallback), even though
/// RevitAPIUI does not. Use this one for tests whose only Revit dependency is a
/// type reference the JIT resolves eagerly (e.g. a cast to Document) — they run
/// for real on a machine with Revit installed, and skip cleanly (never fail)
/// everywhere else, exactly like <see cref="RequiresRevitApiFactAttribute"/>.
/// </summary>
public sealed class RequiresRevitDbApiFactAttribute : FactAttribute
{
    public RequiresRevitDbApiFactAttribute()
    {
        if (!RevitApiAvailability.IsRevitDbApiLoadable)
            Skip = "Requires RevitAPI (reference-only NuGet assembly; present only with a Revit install).";
    }
}

internal static class RevitApiAvailability
{
    private static readonly Lazy<bool> _uiLoadable = new Lazy<bool>(() => Probe("RevitAPIUI"));
    private static readonly Lazy<bool> _dbLoadable = new Lazy<bool>(() => Probe("RevitAPI"));

    public static bool IsRevitUiApiLoadable => _uiLoadable.Value;
    public static bool IsRevitDbApiLoadable => _dbLoadable.Value;

    private static bool Probe(string assemblyName)
    {
        try
        {
            System.Reflection.Assembly.Load(assemblyName);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
