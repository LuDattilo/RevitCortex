using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Always overwrites parameter values when the family already exists — the
/// Document.LoadFamily(string, out Family) overload without this refuses to do
/// that at all, which made reload-after-edit unreachable. See P1.7 in
/// PLAN_CORRECTION.md. Shared by load_family and edit_family (LoadFamily(Document,
/// IFamilyLoadOptions) pushing changes back into the project that opened it via
/// EditFamily).
/// </summary>
public sealed class OverwritingFamilyLoadOptions : IFamilyLoadOptions
{
    private readonly bool _overwrite;
    public OverwritingFamilyLoadOptions(bool overwrite) => _overwrite = overwrite;

    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
        overwriteParameterValues = _overwrite;
        return _overwrite;
    }

    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source,
        out bool overwriteParameterValues)
    {
        source = FamilySource.Family;
        overwriteParameterValues = _overwrite;
        return _overwrite;
    }
}
