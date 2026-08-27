using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a Toposolid (site/ground surface) from a boundary loop — Toposolid.Create
/// is Revit's current topography element (2024+; this connector's floor is 2026, so
/// no version gate is needed). Nothing built a site surface or a hard/soft landscaping
/// platform at all before this.
/// </summary>
[ToolSafety(false, false)]
public class CreateToposolidTool : ICortexTool
{
    public string Name => "create_toposolid";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a Toposolid (site/ground surface) from a closed boundary loop (Toposolid.Create). " +
        "curves is [{type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}] in mm, forming a closed loop. " +
        "toposolidTypeId and levelId are required — list types with list_system_types(category: \"OST_Toposolid\").";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var typeIdLong = input["toposolidTypeId"]?.Value<long?>() ?? 0;
        var levelIdLong = input["levelId"]?.Value<long?>() ?? 0;
        var curvesArray = input["curves"] as JArray;
        if (typeIdLong <= 0 || levelIdLong <= 0 || curvesArray == null || curvesArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "toposolidTypeId, levelId, and a non-empty curves array (closed loop, mm) are required");

        var toposolidType = doc.GetElement(ToolHelpers.ToElementId(typeIdLong)) as ElementType;
        if (toposolidType == null || toposolidType.Category?.Id != new ElementId(BuiltInCategory.OST_Toposolid))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"toposolidTypeId {typeIdLong} is not an OST_Toposolid type",
                suggestion: "List valid ids with list_system_types(category: \"OST_Toposolid\").");

        var level = doc.GetElement(ToolHelpers.ToElementId(levelIdLong)) as Level;
        if (level == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"levelId {levelIdLong} is not a Level");

        var curves = CurveSpecHelpers.ParseCurveSpecsMm(curvesArray, out var curveError);
        if (curveError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, curveError);

        CurveLoop loop;
        try
        {
            loop = CurveLoop.Create(curves);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"curves do not form a valid closed loop: {ex.Message}");
        }

        using var tx = new Transaction(doc, "RiveTT: Create Toposolid");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        Toposolid toposolid;
        try
        {
            toposolid = Toposolid.Create(doc, new List<CurveLoop> { loop }, toposolidType.Id, level.Id);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Toposolid.Create failed: {ex.Message}");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new
        {
            toposolidId = ToolHelpers.GetElementIdValue(toposolid.Id),
            levelName = level.Name
        });
    }
}
