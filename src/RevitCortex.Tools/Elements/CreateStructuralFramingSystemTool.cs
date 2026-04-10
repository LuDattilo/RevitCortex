using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Creates a beam system (structural framing system) from boundary on a level.
/// </summary>
public class CreateStructuralFramingSystemTool : ICortexTool
{
    public string Name => "create_structural_framing_system";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a beam system (structural framing system) from boundary on a level.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var levelName = input["levelName"]?.Value<string>();
        var xMin = input["xMin"]?.Value<double>() ?? 0;
        var xMax = input["xMax"]?.Value<double>() ?? 10000;
        var yMin = input["yMin"]?.Value<double>() ?? 0;
        var yMax = input["yMax"]?.Value<double>() ?? 10000;
        var spacingMm = input["spacing"]?.Value<double>() ?? 1000;
        var beamTypeName = input["beamTypeName"]?.Value<string>();
        var elevationMm = input["elevation"]?.Value<double>() ?? 0;

        if (string.IsNullOrEmpty(levelName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "levelName is required");

        try
        {
            var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
            if (level == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Level '{levelName}' not found");

            // Resolve beam type
            var beamType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => string.IsNullOrEmpty(beamTypeName) ||
                    fs.Name.Equals(beamTypeName, StringComparison.OrdinalIgnoreCase) ||
                    $"{fs.FamilyName}: {fs.Name}".Equals(beamTypeName, StringComparison.OrdinalIgnoreCase));

            if (beamType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No beam type found");

            // Convert to feet
            var x0 = xMin / MmPerFoot;
            var x1 = xMax / MmPerFoot;
            var y0 = yMin / MmPerFoot;
            var y1 = yMax / MmPerFoot;
            var spacing = spacingMm / MmPerFoot;
            var elev = elevationMm / MmPerFoot;

            using var tx = new Transaction(doc, "RevitCortex: Create Structural Framing System");
            tx.Start();

            if (!beamType.IsActive) beamType.Activate();

            var createdBeams = new List<long>();
            var z = level.Elevation + elev;

            // Create beams along Y direction at spacing intervals in X
            var count = (int)Math.Floor((x1 - x0) / spacing) + 1;
            for (int i = 0; i < count; i++)
            {
                var x = x0 + i * spacing;
                if (x > x1) break;

                var start = new XYZ(x, y0, z);
                var end = new XYZ(x, y1, z);
                var line = Line.CreateBound(start, end);

                var beam = doc.Create.NewFamilyInstance(line, beamType, level, StructuralType.Beam);
                if (beam != null) createdBeams.Add(GetIdLong(beam.Id));
            }

            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                beamCount = createdBeams.Count,
                beamTypeName = beamType.Name,
                levelName = level.Name,
                spacingMm,
                beamIds = createdBeams
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
