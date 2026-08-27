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
/// Sets the created and/or demolished phase on one or more elements.
/// IsDynamic = true — only available when the document has phases.
/// Mirrors the fork's SetElementPhaseEventHandler logic.
/// </summary>
[ToolSafety(false, false)]
public class SetElementPhaseTool : ICortexTool
{
    public string Name => "set_element_phase";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Sets the created and/or demolished phase on one or more elements. IsDynamic = true — only available when the document has phases. Mirrors the fork's SetElementPhaseEventHandler logic.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var requests = input["requests"]?.ToObject<List<SetPhaseRequest>>();
        if (requests == null || requests.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "requests array is required",
                suggestion: "Provide [{\"elementId\": 123, \"createdPhaseId\": 456}]");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var results = new List<object>();
        var successCount = 0;
        var failCount = 0;

        if (!session.RequestConfirmation("change phase for", requests.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Set Element Phase");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        try
        {
            foreach (var req in requests)
            {
                var elementId = new ElementId(req.ElementId);
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    results.Add(new { elementId = req.ElementId, success = false,
                        message = $"Element {req.ElementId} not found" });
                    failCount++;
                    continue;
                }

                bool anySet = false;
                bool perReqFail = false;

                // Set created phase
                if (req.CreatedPhaseId.HasValue)
                {
                    var createdPhaseElemId = new ElementId(req.CreatedPhaseId.Value);
                    if (!(doc.GetElement(createdPhaseElemId) is Phase))
                    {
                        results.Add(new { elementId = req.ElementId, success = false,
                            message = $"CreatedPhaseId {req.CreatedPhaseId.Value} is not a valid Phase element" });
                        failCount++;
                        perReqFail = true;
                    }
                    else
                    {
                        var createdParam = element.get_Parameter(BuiltInParameter.PHASE_CREATED);
                        if (createdParam == null || createdParam.IsReadOnly)
                        {
                            results.Add(new { elementId = req.ElementId, success = false,
                                message = "PHASE_CREATED parameter is not available or is read-only on this element" });
                            failCount++;
                            perReqFail = true;
                        }
                        else
                        {
                            createdParam.Set(createdPhaseElemId);
                            anySet = true;
                        }
                    }
                }

                if (perReqFail) continue;

                // Set demolished phase
                if (req.DemolishedPhaseId.HasValue)
                {
                    var demolishedPhaseElemId = new ElementId(req.DemolishedPhaseId.Value);
                    if (!(doc.GetElement(demolishedPhaseElemId) is Phase))
                    {
                        results.Add(new { elementId = req.ElementId, success = false,
                            message = $"DemolishedPhaseId {req.DemolishedPhaseId.Value} is not a valid Phase element" });
                        failCount++;
                        continue;
                    }

                    var demolishedParam = element.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                    if (demolishedParam == null || demolishedParam.IsReadOnly)
                    {
                        results.Add(new { elementId = req.ElementId, success = false,
                            message = "PHASE_DEMOLISHED parameter is not available or is read-only on this element" });
                        failCount++;
                        continue;
                    }

                    demolishedParam.Set(demolishedPhaseElemId);
                    anySet = true;
                }

                if (!anySet)
                {
                    results.Add(new { elementId = req.ElementId, success = false,
                        message = "No phase was specified — provide createdPhaseId and/or demolishedPhaseId" });
                    failCount++;
                }
                else
                {
                    results.Add(new { elementId = req.ElementId, success = true,
                        message = "Phase set successfully" });
                    successCount++;
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();
            throw;
        }

        return CortexResult<object>.Ok(new
        {
            message = $"Set phase on {successCount}/{requests.Count} element(s) successfully",
            successCount,
            failCount,
            results
        });
    }

    private class SetPhaseRequest
    {
        public long ElementId { get; set; }
        public long? CreatedPhaseId { get; set; }
        public long? DemolishedPhaseId { get; set; }

        // The MCP description advertised phaseCreatedId / phaseDemolishedId — the words in
        // the other order — for as long as this tool existed. Newtonsoft simply ignored
        // them, so a caller following the documentation set no phase at all and got a
        // success back. The description is corrected; these aliases keep the code already
        // written against it working instead of leaving it silently inert.
        public long? PhaseCreatedId { set { if (value.HasValue) CreatedPhaseId = value; } }
        public long? PhaseDemolishedId { set { if (value.HasValue) DemolishedPhaseId = value; } }
    }
}
