using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Cuts openings and vertical shafts — floor/wall/slab penetrations and reservations
/// had no entry point at all. Three verified Document.Create.NewOpening overloads,
/// selected by openingType: "shaft" (Level, Level, CurveArray — a vertical shaft
/// spanning levels, e.g. a stair/duct/lift shaft), "host" (Element, CurveArray, bool —
/// a floor/roof penetration cut normal to the host), and "wall" (Wall, XYZ, XYZ — a
/// rectangular opening defined by two corner points on the wall face).
/// </summary>
[ToolSafety(false, false)]
public class CreateOpeningTool : ICortexTool
{
    public string Name => "create_opening";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Cuts an opening or a vertical shaft. openingType=shaft|host|wall. " +
        "shaft: baseLevelId+topLevelId+curves (closed loop, mm plan coords) — a vertical shaft through every " +
        "floor/roof between the two levels. " +
        "host: hostElementId (a floor or roof)+curves (closed loop, mm, in the host's own plane) — cutIsVoid " +
        "defaults to true. " +
        "wall: hostElementId (a wall)+point1+point2 ({x,y,z} mm, two opposite corners on the wall face).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var openingType = (input["openingType"]?.Value<string>() ?? "").ToLowerInvariant();
        try
        {
            return openingType switch
            {
                "shaft" => CreateShaft(doc, input),
                "host" => CreateHostOpening(doc, input),
                "wall" => CreateWallOpening(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unsupported openingType: {openingType}",
                    suggestion: "Use: shaft | host | wall")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> CreateShaft(Document doc, JObject input)
    {
        var baseLevelIdLong = input["baseLevelId"]?.Value<long?>() ?? 0;
        var topLevelIdLong = input["topLevelId"]?.Value<long?>() ?? 0;
        var curvesArray = input["curves"] as JArray;
        if (baseLevelIdLong <= 0 || topLevelIdLong <= 0 || curvesArray == null || curvesArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "baseLevelId, topLevelId, and a non-empty curves array (closed loop, mm) are required");

        var baseLevel = doc.GetElement(ToolHelpers.ToElementId(baseLevelIdLong)) as Level;
        var topLevel = doc.GetElement(ToolHelpers.ToElementId(topLevelIdLong)) as Level;
        if (baseLevel == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{baseLevelIdLong} is not a Level");
        if (topLevel == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{topLevelIdLong} is not a Level");

        var curves = CurveSpecHelpers.ParseCurveSpecsMm(curvesArray, out var curveError);
        if (curveError != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, curveError);

        var curveArray = new CurveArray();
        foreach (var c in curves) curveArray.Append(c);

        using var tx = new Transaction(doc, "RiveTT: Create Shaft Opening");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        Opening opening;
        try
        {
            opening = doc.Create.NewOpening(baseLevel, topLevel, curveArray);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"NewOpening (shaft) failed: {ex.Message}");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new
        {
            openingId = ToolHelpers.GetElementIdValue(opening.Id),
            baseLevel = baseLevel.Name,
            topLevel = topLevel.Name
        });
    }

    private static CortexResult<object> CreateHostOpening(Document doc, JObject input)
    {
        var hostIdLong = input["hostElementId"]?.Value<long?>() ?? 0;
        var curvesArray = input["curves"] as JArray;
        if (hostIdLong <= 0 || curvesArray == null || curvesArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "hostElementId and a non-empty curves array (closed loop, mm) are required");

        var host = doc.GetElement(ToolHelpers.ToElementId(hostIdLong));
        if (host == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"hostElementId {hostIdLong} not found");

        var curves = CurveSpecHelpers.ParseCurveSpecsMm(curvesArray, out var curveError);
        if (curveError != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, curveError);

        var curveArray = new CurveArray();
        foreach (var c in curves) curveArray.Append(c);

        var cutIsVoid = input["cutIsVoid"]?.Value<bool?>() ?? true;

        using var tx = new Transaction(doc, "RiveTT: Create Host Opening");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        Opening opening;
        try
        {
            opening = doc.Create.NewOpening(host, curveArray, cutIsVoid);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"NewOpening (host) failed: {ex.Message}",
                suggestion: "hostElementId must be a floor or roof; the curves must form a closed loop within its boundary.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { openingId = ToolHelpers.GetElementIdValue(opening.Id), hostElementId = hostIdLong });
    }

    private static CortexResult<object> CreateWallOpening(Document doc, JObject input)
    {
        var hostIdLong = input["hostElementId"]?.Value<long?>() ?? 0;
        var p1Token = input["point1"];
        var p2Token = input["point2"];
        if (hostIdLong <= 0 || p1Token == null || p2Token == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "hostElementId, point1, and point2 ({x,y,z} in mm) are required");

        var wall = doc.GetElement(ToolHelpers.ToElementId(hostIdLong)) as Wall;
        if (wall == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"hostElementId {hostIdLong} is not a Wall");

        var p1 = ParseXYZ(p1Token);
        var p2 = ParseXYZ(p2Token);

        using var tx = new Transaction(doc, "RiveTT: Create Wall Opening");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        Opening opening;
        try
        {
            opening = doc.Create.NewOpening(wall, p1, p2);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"NewOpening (wall) failed: {ex.Message}");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { openingId = ToolHelpers.GetElementIdValue(opening.Id), hostElementId = hostIdLong });
    }

    private static XYZ ParseXYZ(JToken token) => new XYZ(
        (token["x"]?.Value<double>() ?? 0) / MmPerFoot,
        (token["y"]?.Value<double>() ?? 0) / MmPerFoot,
        (token["z"]?.Value<double>() ?? 0) / MmPerFoot);
}
