using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Gets or sets project units (length, area, volume, angle, slope, etc.).
/// </summary>
public class ManageProjectUnitsTool : ICortexTool
{
    public string Name => "manage_project_units";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Gets or sets project units. Actions: get (all specs), set (one spec), list_valid_units (available units for a spec type).";

    // All spec types exposed to the user
    private static readonly (string key, ForgeTypeId specId)[] Specs =
    {
        ("length",        SpecTypeId.Length),
        ("area",          SpecTypeId.Area),
        ("volume",        SpecTypeId.Volume),
        ("angle",         SpecTypeId.Angle),
        ("slope",         SpecTypeId.Slope),
        ("number",        SpecTypeId.Number),
        ("currency",      SpecTypeId.Currency),
        ("mass",          SpecTypeId.Mass),
        ("force",         SpecTypeId.Force),
        ("speed",         SpecTypeId.Speed),
        ("temperature",   SpecTypeId.HvacTemperature),
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "get";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "get"               => GetUnits(doc),
                "set"               => SetUnit(doc, input, session),
                "list_valid_units"  => ListValidUnits(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use one of: get, set, list_valid_units")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to manage project units: {ex.Message}");
        }
    }

    private static CortexResult<object> GetUnits(Document doc)
    {
        var units = doc.GetUnits();
        var specResults = new List<object>();

        foreach (var (key, specId) in Specs)
        {
            try
            {
                var opts       = units.GetFormatOptions(specId);
                var unitTypeId = opts.GetUnitTypeId();
                specResults.Add(new
                {
                    specType    = key,
                    displayUnit = LabelUtils.GetLabelForUnit(unitTypeId),
                    unitTypeId  = unitTypeId.TypeId,
                    accuracy    = opts.Accuracy
                });
            }
            catch { /* spec not applicable to this document */ }
        }

        return CortexResult<object>.Ok(new
        {
            specCount = specResults.Count,
            specs = specResults
        });
    }

    private static CortexResult<object> SetUnit(Document doc, JObject input, CortexSession session)
    {
        var specType = input["specType"]?.Value<string>();
        var unit     = input["unit"]?.Value<string>();

        if (string.IsNullOrEmpty(specType))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "specType is required (e.g. length, area, volume, angle)");
        if (string.IsNullOrEmpty(unit))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "unit is required (e.g. meters, millimeters, feet)");

        var specEntry = Specs.FirstOrDefault(s => s.key == specType!.ToLowerInvariant());
        if (specEntry.specId == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown specType '{specType}'",
                suggestion: "Use: " + string.Join(", ", Specs.Select(s => s.key)));

        var unitTypeId = ResolveUnitTypeId(unit!);
        if (unitTypeId == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown unit '{unit}'. Use list_valid_units to see available options for this spec.");

        // Validate unit is applicable to this spec
        var validUnits = UnitUtils.GetValidUnits(specEntry.specId);
        if (!validUnits.Any(u => u.TypeId == unitTypeId.TypeId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unit '{unit}' is not valid for specType '{specType}'");

        var units = doc.GetUnits();
        var opts  = units.GetFormatOptions(specEntry.specId);
        opts.SetUnitTypeId(unitTypeId);

        // Optional overrides
        var accuracy = input["accuracy"]?.Value<double?>();
        if (accuracy.HasValue) opts.Accuracy = accuracy.Value;

        units.SetFormatOptions(specEntry.specId, opts);

        if (!session.RequestConfirmation("set project units", 1, $"{specType} -> {unit}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Project Units");
        tx.Start();
        doc.SetUnits(units);
        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action      = "set",
            specType,
            displayUnit = LabelUtils.GetLabelForUnit(unitTypeId),
            unitTypeId  = unitTypeId.TypeId
        });
    }

    private static CortexResult<object> ListValidUnits(Document doc, JObject input)
    {
        var specType = input["specType"]?.Value<string>() ?? "length";

        var specEntry = Specs.FirstOrDefault(s => s.key == specType.ToLowerInvariant());
        if (specEntry.specId == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown specType '{specType}'",
                suggestion: "Use: " + string.Join(", ", Specs.Select(s => s.key)));

        var validUnits = UnitUtils.GetValidUnits(specEntry.specId);
        var result = validUnits.Select(u => new
        {
            unitTypeId  = u.TypeId,
            displayName = TryGetLabel(u)
        }).ToList();

        return CortexResult<object>.Ok(new
        {
            specType,
            unitCount = result.Count,
            units = result
        });
    }

    private static string TryGetLabel(ForgeTypeId id)
    {
        try { return LabelUtils.GetLabelForUnit(id); }
        catch { return id.TypeId; }
    }

    private static ForgeTypeId? ResolveUnitTypeId(string unit) =>
        unit.ToLowerInvariant() switch
        {
            "meters"          or "m"          => UnitTypeId.Meters,
            "millimeters"     or "mm"         => UnitTypeId.Millimeters,
            "centimeters"     or "cm"         => UnitTypeId.Centimeters,
            "feet"            or "ft"         => UnitTypeId.Feet,
            "inches"          or "in"         => UnitTypeId.Inches,
            "feet_fractional_inches" or "feet_inches" => UnitTypeId.FeetFractionalInches,
            "square_meters"   or "sqm"        => UnitTypeId.SquareMeters,
            "square_feet"     or "sqft"       => UnitTypeId.SquareFeet,
            "square_millimeters" or "sqmm"    => UnitTypeId.SquareMillimeters,
            "square_centimeters" or "sqcm"    => UnitTypeId.SquareCentimeters,
            "cubic_meters"    or "cbm"        => UnitTypeId.CubicMeters,
            "cubic_feet"      or "cbft"       => UnitTypeId.CubicFeet,
            "cubic_millimeters" or "cbmm"     => UnitTypeId.CubicMillimeters,
            "liters"          or "l"          => UnitTypeId.Liters,
            "degrees"         or "deg"        => UnitTypeId.Degrees,
            "radians"         or "rad"        => UnitTypeId.Radians,
            "percent"         or "%"          => UnitTypeId.Percentage,
            "kilograms"       or "kg"         => UnitTypeId.Kilograms,
            "kilograms_force" or "kgf"        => UnitTypeId.KilogramsForce,
            "newtons"         or "n"          => UnitTypeId.Newtons,
            "celsius"         or "°c"         => UnitTypeId.Celsius,
            "fahrenheit"      or "°f"         => UnitTypeId.Fahrenheit,
            _                                 => null
        };
}
