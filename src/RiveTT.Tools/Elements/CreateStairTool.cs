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
/// Creates a component stair between two levels.
///
/// Why this exists now: the connector documented stairs as impossible because
/// "the standard Revit stair goes through a modal sketch editor". That is true of
/// stair-BY-SKETCH only. A component stair is built through
/// <see cref="StairsEditScope"/>, which is an API edit scope — it behaves like a
/// TransactionGroup, opens no UI, and is Autodesk's documented way to create
/// stairs programmatically. A building with vertical circulation was simply out
/// of reach for no reason.
///
/// Contract: StairsEditScope must be started with NO transaction open, runs are
/// created in transactions INSIDE the scope, and the scope is committed with a
/// failure preprocessor so a warning cannot open a modal dialog.
/// </summary>
[ToolSafety(false, false)]
public sealed class CreateStairTool : ICortexTool
{

    public string Name => "create_stair";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Creates a native component stair between two levels from one or more straight runs. " +
        "runs is [{p0:{x,y}, p1:{x,y}}, ...] in mm (plan coordinates; the levels drive the elevation). " +
        "Consecutive runs get an automatic landing. Optionally applies a stair type, a run width and a " +
        "railing. The response reports the riser count Revit actually produced against the one it wanted.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var baseLevelId = input["baseLevelId"]?.Value<long>() ?? 0;
        var topLevelId = input["topLevelId"]?.Value<long>() ?? 0;
        var stairsTypeId = input["stairsTypeId"]?.Value<long>() ?? 0;
        var railingTypeId = input["railingTypeId"]?.Value<long>() ?? 0;
        var widthMm = input["widthMm"]?.Value<double>() ?? 0;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (baseLevelId <= 0 || topLevelId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "baseLevelId and topLevelId are both required",
                suggestion: "Read the level ids from get_project_info.");

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

        if (!TryReadRuns(input["runs"], baseLevel.Elevation, out var runLines, out var runError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, runError);

        StairsType? stairsType = null;
        if (stairsTypeId > 0)
        {
            stairsType = doc.GetElement(ToolHelpers.ToElementId(stairsTypeId)) as StairsType;
            if (stairsType == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"stairsTypeId {stairsTypeId} is not a StairsType",
                    suggestion: "List the available ones with list_system_types(category: \"OST_Stairs\").");
        }

        var heightFt = topLevel.Elevation - baseLevel.Elevation;
        var maxRiserFt = MaxRiserHeightFt(doc, stairsType);
        var estimatedRisers = maxRiserFt > 0 ? (int)Math.Ceiling(heightFt / maxRiserFt) : 0;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: a {runLines.Count}-run stair would be created from '{baseLevel.Name}' to " +
                          $"'{topLevel.Name}' ({heightFt * MmPerFoot:F0} mm)." +
                          (runLines.Count > 1 ? $" {runLines.Count - 1} automatic landing(s)." : ""),
                baseLevel = baseLevel.Name,
                topLevel = topLevel.Name,
                heightMm = Math.Round(heightFt * MmPerFoot, 1),
                runCount = runLines.Count,
                landingCount = Math.Max(0, runLines.Count - 1),
                estimatedRiserCount = estimatedRisers,
                maxRiserHeightMm = maxRiserFt > 0 ? Math.Round(maxRiserFt * MmPerFoot, 1) : (double?)null,
                totalRunLengthMm = Math.Round(runLines.Sum(line => line.Length) * MmPerFoot, 1),
                stairsTypeId = stairsTypeId > 0 ? (long?)stairsTypeId : null,
                widthMm = widthMm > 0 ? (double?)widthMm : null,
                railingTypeId = railingTypeId > 0 ? (long?)railingTypeId : null,
                warnings = estimatedRisers > 0 && runLines.Count == 1 &&
                           runLines[0].Length * MmPerFoot < estimatedRisers * 250
                    ? new[]
                    {
                        $"The single run is {runLines[0].Length * MmPerFoot:F0} mm long for about " +
                        $"{estimatedRisers} risers: Revit will not reach the top level and will report fewer " +
                        "risers than needed. Lengthen the run or split it into several runs."
                    }
                    : Array.Empty<string>()
            });
        }

        StairsEditScope? scope = null;
        try
        {
            // The edit scope behaves like a transaction group: it must be started
            // with no transaction open, and the runs are created inside it.
            scope = new StairsEditScope(doc, "RiveTT: Create Stair");
            var stairsId = scope.Start(baseLevel.Id, topLevel.Id);

            var runIds = new List<long>();
            var landingIds = new List<long>();
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, "RiveTT: Stair Runs"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                if (stairsType != null)
                {
                    var stairs = doc.GetElement(stairsId);
                    if (stairs != null && stairs.GetTypeId() != stairsType.Id)
                        stairs.ChangeTypeId(stairsType.Id);
                }

                StairsRun? previousRun = null;
                foreach (var line in runLines)
                {
                    var run = StairsRun.CreateStraightRun(doc, stairsId, line, StairsRunJustification.Center);
                    if (widthMm > 0) run.ActualRunWidth = widthMm / MmPerFoot;
                    runIds.Add(ToolHelpers.GetElementIdValue(run.Id));

                    if (previousRun != null)
                    {
                        try
                        {
                            // Revit may produce several landings for one junction.
                            var landings = StairsLanding.CreateAutomaticLanding(doc, previousRun.Id, run.Id);
                            if (landings != null)
                                landingIds.AddRange(landings.Select(ToolHelpers.GetElementIdValue));
                        }
                        catch (Exception exception)
                        {
                            // A landing Revit refuses is not a reason to lose the runs.
                            warnings.Add($"Automatic landing between two runs failed: {exception.Message}");
                        }
                    }

                    previousRun = run;
                }

                if (tx.Commit() != TransactionStatus.Committed)
                {
                    scope.Cancel();
                    scope = null;
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the stair runs: {TransactionFailureHandling.Describe(txFailures)}",
                        // Both refusals observed in practice came from these two,
                        // and the raw Revit text ("Impossible de créer l'escalier")
                        // names neither.
                        suggestion: "Two usual causes: (1) the run length does not fit the riser count needed " +
                                    $"for {heightFt * MmPerFoot:F0} mm — allow roughly tread depth x " +
                                    "(risers - 1); (2) the stair type is a catalogued precast type with a " +
                                    "fixed height and step count, which refuses an arbitrary level-to-level " +
                                    "height — pick a cast-in-place or assembled type from " +
                                    "list_system_types(OST_Stairs).");
                }
            }

            // Commit with a preprocessor: a stair commonly raises warnings, and an
            // unhandled one would open a modal dialog and freeze the MCP bridge.
            var scopeFailures = new TransactionFailureHandling.FailureCapture();
            scope.Commit(scopeFailures);
            scope = null;

            var created = doc.GetElement(ToolHelpers.ToElementId(ToolHelpers.GetElementIdValue(stairsId))) as Stairs;
            var actualRisers = created?.ActualRisersNumber ?? 0;
            var desiredRisers = created?.DesiredRisersNumber ?? 0;

            // Railings are created OUTSIDE the edit scope, and Revit creates one per
            // side of the stair. Most stair types create their own, in which case
            // Railing.Create fails with "already has associated railings" — which
            // reads as a tool error when it really means "nothing to do".
            var railingIds = new List<long>();
            string? railingError = null;
            var existingRailings = new List<long>();
            try
            {
                if (created != null)
                    existingRailings.AddRange(
                        created.GetAssociatedRailings().Select(ToolHelpers.GetElementIdValue));
            }
            catch
            {
                // Reading the associated railings is context, never the answer.
            }

            if (railingTypeId > 0 && existingRailings.Count > 0)
            {
                railingIds.AddRange(existingRailings);
                warnings.Add($"The stair type already created {existingRailings.Count} railing(s), so " +
                             "railingTypeId was not applied. Retype them with change_element_type if needed.");
            }
            else if (railingTypeId > 0)
            {
                try
                {
                    using var railingTx = new Transaction(doc, "RiveTT: Stair Railing");
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

            if (actualRisers > 0 && desiredRisers > 0 && actualRisers != desiredRisers)
            {
                // Direction matters: MORE risers than needed means the run is too
                // long and overshoots the level. Telling the caller to lengthen a run
                // that is already too long is worse than saying nothing.
                var treadMm = created == null ? 0 : created.ActualTreadDepth * MmPerFoot;
                var deltaRisers = actualRisers - desiredRisers;
                var correctionMm = treadMm > 0 ? Math.Abs(deltaRisers) * treadMm : 0;

                warnings.Add(deltaRisers > 0
                    ? $"The stair has {actualRisers} risers but only {desiredRisers} are needed to reach " +
                      $"'{topLevel.Name}': the run is too LONG and overshoots the level" +
                      (correctionMm > 0 ? $" — shorten it by about {correctionMm:F0} mm" : "") +
                      ". Revit created the stair anyway."
                    : $"The stair has {actualRisers} risers but needs {desiredRisers} to reach " +
                      $"'{topLevel.Name}': the run is too SHORT and stops below the level" +
                      (correctionMm > 0 ? $" — lengthen it by about {correctionMm:F0} mm" : "") +
                      ", or add a second run. Revit created the stair anyway.");
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Created a stair from '{baseLevel.Name}' to '{topLevel.Name}' " +
                          $"({runIds.Count} run(s), {landingIds.Count} landing(s), {actualRisers} riser(s)).",
                stairsId = ToolHelpers.GetElementIdValue(stairsId),
                runIds,
                landingIds,
                actualRiserCount = actualRisers,
                desiredRiserCount = desiredRisers,
                reachesTopLevel = desiredRisers == 0 || actualRisers == desiredRisers,
                actualTreadDepthMm = created == null ? (double?)null : Math.Round(created.ActualTreadDepth * MmPerFoot, 1),
                actualRiserHeightMm = created == null ? (double?)null : Math.Round(created.ActualRiserHeight * MmPerFoot, 1),
                railingIds,
                railingError,
                scopeWarnings = scopeFailures.Warnings.Take(10).ToList(),
                warnings
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create the stair: {exception.Message}",
                suggestion: "A component stair needs two distinct levels, a run that fits in the model, and no " +
                            "other edit scope open. Check the run geometry and retry with dryRun first.");
        }
        finally
        {
            // Never leave an edit scope open: Revit would stay in stair edit mode
            // for the rest of the session.
            try { scope?.Cancel(); } catch { }
        }
    }

    private static bool TryReadRuns(JToken? token, double baseElevationFt, out List<Line> runs, out string error)
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

            // The run's location line must sit at the base level's elevation, not
            // at the project's absolute Z = 0 — see P0.1 in PLAN_CORRECTION.md.
            var startPoint = new XYZ(
                (start["x"]?.Value<double>() ?? 0) / MmPerFoot,
                (start["y"]?.Value<double>() ?? 0) / MmPerFoot,
                baseElevationFt);
            var endPoint = new XYZ(
                (end["x"]?.Value<double>() ?? 0) / MmPerFoot,
                (end["y"]?.Value<double>() ?? 0) / MmPerFoot,
                baseElevationFt);

            if (startPoint.DistanceTo(endPoint) < 1e-6)
            {
                error = "a run cannot have coincident start and end points";
                return false;
            }

            runs.Add(Line.CreateBound(startPoint, endPoint));
        }

        return true;
    }

    private static double MaxRiserHeightFt(Document doc, StairsType? stairsType)
    {
        var type = stairsType ?? new FilteredElementCollector(doc)
            .OfClass(typeof(StairsType))
            .Cast<StairsType>()
            .FirstOrDefault();

        if (type == null) return 0;
        try { return type.MaxRiserHeight; } catch { return 0; }
    }
}
