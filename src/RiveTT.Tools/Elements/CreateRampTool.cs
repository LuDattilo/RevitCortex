using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a native component ramp between two levels — the accessibility-critical
/// gap create_stair left open. Modern Revit has no separate ramp creation API: a ramp
/// IS a StairsEditScope/StairsRun component whose applied type belongs to OST_Ramps
/// instead of OST_Stairs (confirmed across multiple sources; there is no distinct
/// "RampEditScope" or "RampRun" class). This tool is create_stair's mechanism with a
/// ramp type required and enforced, and with the near-zero max riser height a ramp
/// implies instead of stair-sized risers.
/// </summary>
[ToolSafety(false, false)]
public sealed class CreateRampTool : ICortexTool
{

    public string Name => "create_ramp";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Creates a native component ramp between two levels from one or more straight runs (StairsEditScope " +
        "with a ramp type applied — Revit has no separate ramp API). runs is [{p0:{x,y}, p1:{x,y}}, ...] in mm " +
        "(plan coordinates; the levels drive the elevation). rampTypeId is REQUIRED and must be an OST_Ramps " +
        "type (list_system_types(category:\"OST_Ramps\")); passing a stair type produces a stair, not a ramp. " +
        "Optionally sets a run width and a railing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var baseLevelId = input["baseLevelId"]?.Value<long>() ?? 0;
        var topLevelId = input["topLevelId"]?.Value<long>() ?? 0;
        var rampTypeIdLong = input["rampTypeId"]?.Value<long>() ?? 0;
        var railingTypeId = input["railingTypeId"]?.Value<long>() ?? 0;
        var widthMm = input["widthMm"]?.Value<double>() ?? 0;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (baseLevelId <= 0 || topLevelId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "baseLevelId and topLevelId are both required",
                suggestion: "Read the level ids from get_project_info.");
        if (rampTypeIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "rampTypeId is required",
                suggestion: "List OST_Ramps types with list_system_types(category: \"OST_Ramps\").");

        var baseLevel = doc.GetElement(ToolHelpers.ToElementId(baseLevelId)) as Level;
        var topLevel = doc.GetElement(ToolHelpers.ToElementId(topLevelId)) as Level;
        if (baseLevel == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"baseLevelId {baseLevelId} is not a Level");
        if (topLevel == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"topLevelId {topLevelId} is not a Level");
        if (topLevel.Elevation <= baseLevel.Elevation)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"topLevel '{topLevel.Name}' ({topLevel.Elevation * MmPerFoot:F0} mm) must be ABOVE baseLevel " +
                $"'{baseLevel.Name}' ({baseLevel.Elevation * MmPerFoot:F0} mm)");

        var rampType = doc.GetElement(ToolHelpers.ToElementId(rampTypeIdLong)) as ElementType;
        if (rampType == null || rampType.Category?.Id != new ElementId(BuiltInCategory.OST_Ramps))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"rampTypeId {rampTypeIdLong} is not an OST_Ramps type",
                suggestion: "List valid ids with list_system_types(category: \"OST_Ramps\").");

        if (!TryReadRuns(input["runs"], out var runLines, out var runError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, runError);

        var heightFt = topLevel.Elevation - baseLevel.Elevation;
        var totalRunLengthFt = runLines.Sum(l => l.Length);
        // A ramp's slope is a run-length/height ratio, not a riser count: PMR guidance
        // (and most building codes) cap it around 1:12 (~8.3%); flag anything steeper
        // instead of silently producing a ramp nobody can use.
        var slopePercent = totalRunLengthFt > 0 ? heightFt / totalRunLengthFt * 100.0 : double.PositiveInfinity;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: a {runLines.Count}-run ramp would be created from '{baseLevel.Name}' to " +
                          $"'{topLevel.Name}' ({heightFt * MmPerFoot:F0} mm), slope {slopePercent:F1}%.",
                baseLevel = baseLevel.Name,
                topLevel = topLevel.Name,
                heightMm = Math.Round(heightFt * MmPerFoot, 1),
                runCount = runLines.Count,
                totalRunLengthMm = Math.Round(totalRunLengthFt * MmPerFoot, 1),
                slopePercent = Math.Round(slopePercent, 1),
                rampTypeId = rampTypeIdLong,
                widthMm = widthMm > 0 ? (double?)widthMm : null,
                railingTypeId = railingTypeId > 0 ? (long?)railingTypeId : null,
                warnings = slopePercent > 8.33
                    ? new[] { $"Slope is {slopePercent:F1}%, steeper than the common 1:12 (8.3%) PMR/code limit. " +
                               "Lengthen the run(s) or add a run to reach the top level less steeply." }
                    : Array.Empty<string>()
            });
        }

        StairsEditScope? scope = null;
        try
        {
            scope = new StairsEditScope(doc, "RiveTT: Create Ramp");
            var stairsId = scope.Start(baseLevel.Id, topLevel.Id);

            var runIds = new List<long>();
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, "RiveTT: Ramp Runs"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                var stairs = doc.GetElement(stairsId);
                if (stairs != null && stairs.GetTypeId() != rampType.Id)
                    stairs.ChangeTypeId(rampType.Id);

                StairsRun? previousRun = null;
                foreach (var line in runLines)
                {
                    var run = StairsRun.CreateStraightRun(doc, stairsId, line, StairsRunJustification.Center);
                    if (widthMm > 0) run.ActualRunWidth = widthMm / MmPerFoot;
                    runIds.Add(ToolHelpers.GetElementIdValue(run.Id));
                    previousRun = run;
                }

                if (tx.Commit() != TransactionStatus.Committed)
                {
                    scope.Cancel();
                    scope = null;
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the ramp runs: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "The run length likely does not fit the height/slope the applied ramp type " +
                                    "allows; lengthen the run(s) or pick a different OST_Ramps type.");
                }
            }

            var scopeFailures = new TransactionFailureHandling.FailureCapture();
            scope.Commit(scopeFailures);
            scope = null;

            var created = doc.GetElement(ToolHelpers.ToElementId(ToolHelpers.GetElementIdValue(stairsId))) as Stairs;

            var railingIds = new List<long>();
            string? railingError = null;
            var existingRailings = new List<long>();
            try
            {
                if (created != null)
                    existingRailings.AddRange(created.GetAssociatedRailings().Select(ToolHelpers.GetElementIdValue));
            }
            catch { /* context only */ }

            if (railingTypeId > 0 && existingRailings.Count > 0)
            {
                railingIds.AddRange(existingRailings);
                warnings.Add($"The ramp type already created {existingRailings.Count} railing(s), so " +
                             "railingTypeId was not applied. Retype them with change_element_type if needed.");
            }
            else if (railingTypeId > 0)
            {
                try
                {
                    using var railingTx = new Transaction(doc, "RiveTT: Ramp Railing");
                    TransactionFailureHandling.SuppressWarnings(railingTx);
                    railingTx.Start();
                    var railings = Railing.Create(doc, stairsId, ToolHelpers.ToElementId(railingTypeId),
                        RailingPlacementPosition.Treads);
                    railingTx.Commit();
                    if (railings != null)
                        railingIds.AddRange(railings.Select(ToolHelpers.GetElementIdValue));
                }
                catch (Exception exception)
                {
                    railingError = exception.Message;
                }
            }
            else
            {
                railingIds.AddRange(existingRailings);
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Created a ramp from '{baseLevel.Name}' to '{topLevel.Name}' " +
                          $"({runIds.Count} run(s), slope {slopePercent:F1}%).",
                rampId = ToolHelpers.GetElementIdValue(stairsId),
                runIds,
                slopePercent = Math.Round(slopePercent, 1),
                railingIds,
                railingError,
                scopeWarnings = scopeFailures.Warnings.Take(10).ToList(),
                warnings
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create the ramp: {exception.Message}",
                suggestion: "A component ramp needs two distinct levels, an OST_Ramps type, a run that fits the " +
                            "model, and no other edit scope open. Check the run geometry and retry with dryRun first.");
        }
        finally
        {
            try { scope?.Cancel(); } catch { }
        }
    }

    private static bool TryReadRuns(JToken? token, out List<Line> runs, out string error)
    {
        runs = new List<Line>();
        error = "";

        if (token is not JArray array || array.Count == 0)
        {
            error = "runs is required: [{p0:{x,y}, p1:{x,y}}, ...] in mm";
            return false;
        }

        foreach (var item in array)
        {
            if (item is not JObject run || run["p0"] is not JObject start || run["p1"] is not JObject end)
            {
                error = "each run must be {p0:{x,y}, p1:{x,y}} in mm";
                return false;
            }

            var startPoint = new XYZ(
                (start["x"]?.Value<double>() ?? 0) / MmPerFoot,
                (start["y"]?.Value<double>() ?? 0) / MmPerFoot,
                0);
            var endPoint = new XYZ(
                (end["x"]?.Value<double>() ?? 0) / MmPerFoot,
                (end["y"]?.Value<double>() ?? 0) / MmPerFoot,
                0);

            if (startPoint.DistanceTo(endPoint) < 1e-6)
            {
                error = "a run cannot have coincident start and end points";
                return false;
            }

            runs.Add(Line.CreateBound(startPoint, endPoint));
        }

        return true;
    }
}
