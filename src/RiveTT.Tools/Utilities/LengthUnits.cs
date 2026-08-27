namespace RiveTT.Tools.Utilities;

/// <summary>
/// The single conversion factor between millimetres (every tool's input/output
/// unit) and feet (Revit's internal unit). Exact by definition — 1 foot is
/// 304.8 mm, with nothing left to round — so centralizing it changes no value;
/// it removes the 60+ files that each declared their own private copy of the
/// same literal. See P3.2 in PLAN_CORRECTION.md: this is hygiene, not a
/// correctness fix — the duplicated literal was never the cause of any
/// measured defect.
/// </summary>
public static class LengthUnits
{
    public const double MmPerFoot = 304.8;
}
