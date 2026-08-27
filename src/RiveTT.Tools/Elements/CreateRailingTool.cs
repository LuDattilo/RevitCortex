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

/// <summary>Creates an independent guardrail from a horizontal, connected path.</summary>
[ToolSafety(false, false)]
public sealed class CreateRailingTool : ICortexTool
{

    public string Name => "create_railing";
    public string Category => "Architecture";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a native Revit guardrail from a connected horizontal path. A railing type and base level " +
        "are required. ELEVATION: the path z values only have to be equal to each other — baseLevelId sets " +
        "the height, exactly like create_wall. Pass z=0 and choose the level.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var document = ToolHelpers.GetDocument(session);
        if (document == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var path = input["path"] as JArray;
        var railingTypeId = input["railingTypeId"]?.Value<long>() ?? -1;
        var baseLevelId = input["baseLevelId"]?.Value<long>() ?? -1;
        if (path == null || path.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "path must contain at least two points");
        if (railingTypeId <= 0 || baseLevelId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "railingTypeId and baseLevelId are required");

        var railingType = document.GetElement(ToolHelpers.ToElementId(railingTypeId)) as RailingType;
        var level = document.GetElement(ToolHelpers.ToElementId(baseLevelId)) as Level;
        if (railingType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Railing type {railingTypeId} was not found");
        if (level == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Level {baseLevelId} was not found");

        try
        {
            var points = path.Select(ToXyz).ToList();
            if (points.Zip(points.Skip(1), (a, b) => Math.Abs(a.Z - b.Z) > 1e-8).Any(x => x))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "A railing path must be horizontal");

            var curveLoop = new CurveLoop();
            for (var index = 0; index < points.Count - 1; index++)
                curveLoop.Append(Line.CreateBound(points[index], points[index + 1]));

            if (ToolHelpers.GetDryRun(input))
                return CortexResult<object>.Ok(new
                {
                    dryRun = true,
                    railingTypeId,
                    railingType = railingType.Name,
                    baseLevelId,
                    level = level.Name,
                    segmentCount = points.Count - 1
                });

            using var transaction = new Transaction(document, "RiveTT: Create Railing");
            var failures = TransactionFailureHandling.FromInput(transaction, input);
            transaction.Start();
            var railing = Railing.Create(document, curveLoop, railingType.Id, level.Id);
            if (transaction.Commit() != TransactionStatus.Committed)
                return TransactionFailureHandling.ToFailure(failures,
                    "Railing creation was rolled back", "Check path continuity and railing constraints.");

            return CortexResult<object>.Ok(new
            {
                railingId = ToolHelpers.GetElementIdValue(railing.Id),
                railingType = railingType.Name,
                level = level.Name
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not create railing: {exception.Message}");
        }
    }

    private static XYZ ToXyz(JToken point) => new(
        (point["x"]?.Value<double>() ?? 0) / MmPerFoot,
        (point["y"]?.Value<double>() ?? 0) / MmPerFoot,
        (point["z"]?.Value<double>() ?? 0) / MmPerFoot);
}
