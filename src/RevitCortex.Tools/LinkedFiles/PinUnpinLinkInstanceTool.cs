using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Pins or unpins one or more link instances.
/// </summary>
[ToolSafety(false, false)]
public class PinUnpinLinkInstanceTool : ICortexTool
{
    public string Name => "pin_unpin_link_instance";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Pins or unpins one or more linked file instances to prevent or allow accidental movement.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var instanceIds = input["instanceIds"]?.ToObject<List<long>>() ?? new List<long>();
        var pin = input["pin"]?.Value<bool>() ?? true;

        if (instanceIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "instanceIds array is required");

        var action = pin ? "pin" : "unpin";
        if (!session.RequestConfirmation($"{action} link instance(s)", instanceIds.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var results = new List<object>();
            int successCount = 0;

            using var tx = new Transaction(doc, $"RevitCortex: {(pin ? "Pin" : "Unpin")} Link Instance");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var id in instanceIds)
            {
#if REVIT2024_OR_GREATER
                var element = doc.GetElement(new ElementId(id));
#else
                var element = doc.GetElement(new ElementId((int)id));
#endif
                var linkInstance = element as RevitLinkInstance;
                if (linkInstance == null)
                {
                    results.Add(new { instanceId = id, success = false, message = "Not a RevitLinkInstance" });
                    continue;
                }

                linkInstance.Pinned = pin;
                results.Add(new { instanceId = id, success = true, name = linkInstance.Name, pinned = pin });
                successCount++;
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                message = $"{(pin ? "Pinned" : "Unpinned")} {successCount}/{instanceIds.Count} instance(s)",
                action,
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
