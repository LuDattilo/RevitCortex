using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Parses the {type:line|arc, start, end, mid?} curve-spec JSON contract shared by
/// several tools (revision clouds, area boundaries, toposolids, openings). Extracted
/// from the rebar tools' own helper when the Rebar/StructuralSteel surface was removed
/// from this connector, since these callers have nothing to do with reinforcement.
/// </summary>
public static class CurveSpecHelpers
{

    public static double ToMm(double feet) => feet * MmPerFoot;
    public static double FromMm(double mm) => mm / MmPerFoot;

    public static XYZ ParseXyzMm(JToken token)
    {
        var x = token["x"]?.Value<double?>() ?? 0;
        var y = token["y"]?.Value<double?>() ?? 0;
        var z = token["z"]?.Value<double?>() ?? 0;
        return new XYZ(FromMm(x), FromMm(y), FromMm(z));
    }

    public static IList<Curve> ParseCurveSpecsMm(JArray specs, out string? error)
    {
        error = null;
        var curves = new List<Curve>();
        foreach (var item in specs.OfType<JObject>())
        {
            var type = (item["type"]?.Value<string>() ?? "line").Trim().ToLowerInvariant();
            try
            {
                if (type == "line")
                    curves.Add(Line.CreateBound(ParseXyzMm(item["start"]!), ParseXyzMm(item["end"]!)));
                else if (type == "arc")
                    curves.Add(Arc.Create(ParseXyzMm(item["start"]!), ParseXyzMm(item["end"]!), ParseXyzMm(item["mid"]!)));
                else { error = $"Unknown curve type '{type}'. Use 'line' or 'arc'."; return curves; }
            }
            catch (Exception ex) { error = $"Invalid curve geometry: {ex.Message}"; return curves; }
        }
        if (curves.Count == 0) error = "No curves parsed from spec array.";
        return curves;
    }

    public static JObject XyzToDtoMm(XYZ p) => new JObject
    {
        ["x"] = ToMm(p.X), ["y"] = ToMm(p.Y), ["z"] = ToMm(p.Z)
    };

    public static JObject CurveToDtoMm(Curve c)
    {
        var dto = new JObject
        {
            ["type"] = c is Arc ? "arc" : (c is Line ? "line" : c.GetType().Name.ToLowerInvariant()),
            ["start"] = XyzToDtoMm(c.GetEndPoint(0)),
            ["end"] = XyzToDtoMm(c.GetEndPoint(1)),
            ["lengthMm"] = ToMm(c.Length)
        };
        if (c is Arc arc) dto["mid"] = XyzToDtoMm(arc.Evaluate(0.5, true));
        return dto;
    }
}
