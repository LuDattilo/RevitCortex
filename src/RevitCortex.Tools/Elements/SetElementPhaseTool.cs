using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Sets the created and/or demolished phase on one or more elements.
/// IsDynamic = true — only available when the document has phases.
/// Mirrors the fork's SetElementPhaseEventHandler logic.
/// </summary>
public class SetElementPhaseTool : ICortexTool
{
    public string Name => "set_element_phase";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;

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

        using var tx = new Transaction(doc, "RevitCortex: Set Element Phase");
        tx.Start();

        try
        {
            foreach (var req in requests)
            {
#if REVIT2024_OR_GREATER
                var elementId = new ElementId(req.ElementId);
#else
                var elementId = new ElementId((int)req.ElementId);
#endif
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
#if REVIT2024_OR_GREATER
                    var createdPhaseElemId = new ElementId(req.CreatedPhaseId.Value);
#else
                    var createdPhaseElemId = new ElementId((int)req.CreatedPhaseId.Value);
#endif
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
#if REVIT2024_OR_GREATER
                    var demolishedPhaseElemId = new ElementId(req.DemolishedPhaseId.Value);
#else
                    var demolishedPhaseElemId = new ElementId((int)req.DemolishedPhaseId.Value);
#endif
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

            tx.Commit();
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
    }
}
