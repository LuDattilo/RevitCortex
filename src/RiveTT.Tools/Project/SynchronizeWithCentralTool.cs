using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Synchronizes the local model with the workshared central — the one write action in
/// this connector whose blast radius is the WHOLE TEAM's model, not just the local
/// session. Gated by the same ribbon write lock as every other write tool
/// (ToolSafety(false, true) below), AND defaults to dryRun so a caller must pass
/// dryRun:false explicitly to actually push. There is no separate "are you sure" layer
/// beyond that: the write lock and dryRun default are the confirmation.
/// </summary>
[ToolSafety(false, true)]
public class SynchronizeWithCentralTool : ICortexTool
{
    public string Name => "synchronize_with_central";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Synchronizes the local model with the workshared central file (Document.SynchronizeWithCentral). " +
        "AFFECTS THE WHOLE TEAM, not just this session: every other user's next sync pulls what this pushes, " +
        "and it cannot be undone from here. Requires the ribbon write lock (Écriture) AND dryRun:false — " +
        "dryRun defaults to true and only reports whether the document is workshared and has pending local " +
        "changes to relinquish, without touching the central file. Only usable on a workshared document.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        if (!doc.IsWorkshared)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "This document is not workshared: there is no central to synchronize with");

        var dryRun = input["dryRun"]?.Value<bool?>() ?? true;
        var comment = input["comment"]?.Value<string>();
        var relinquishAll = input["relinquishAll"]?.Value<bool?>() ?? true;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = "DryRun: would synchronize with central" +
                          (relinquishAll ? ", relinquishing all worksets/elements/checked-out items" : "") +
                          ". No change made. Pass dryRun:false to actually synchronize.",
                isWorkshared = true,
                centralPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(doc.GetWorksharingCentralModelPath()),
                relinquishAll,
                comment
            });
        }

        try
        {
            var transactOptions = new TransactWithCentralOptions();
            var syncOptions = new SynchronizeWithCentralOptions();
            if (!string.IsNullOrWhiteSpace(comment)) syncOptions.Comment = comment;

            var relinquish = new RelinquishOptions(relinquishAll);
            syncOptions.SetRelinquishOptions(relinquish);

            doc.SynchronizeWithCentral(transactOptions, syncOptions);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"SynchronizeWithCentral failed: {ex.Message}",
                suggestion: "The central file may be locked by another user's sync, or local changes conflict " +
                            "with the central. Resolve the reported issue in Revit and retry.");
        }

        return CortexResult<object>.Ok(new
        {
            message = "Synchronized with central.",
            relinquishAll,
            comment
        });
    }
}
