using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Workflows;

/// <summary>
/// Auto-creates sheets with title blocks from a sheet definition list.
/// </summary>
[ToolSafety(false, false)]
public class WorkflowSheetSetTool : ICortexTool
{
    public string Name => "workflow_sheet_set";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Auto-creates sheets with title blocks from a sheet definition list.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheets = input["sheets"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var titleBlockName = input["titleBlockName"]?.Value<string>();

        if (sheets.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheets array required");

        try
        {
            // Find title block
            var tbs = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            var titleBlock = !string.IsNullOrEmpty(titleBlockName)
                ? tbs.FirstOrDefault(t => t.Name.Equals(titleBlockName, StringComparison.OrdinalIgnoreCase)
                    || t.FamilyName.Equals(titleBlockName, StringComparison.OrdinalIgnoreCase))
                  ?? tbs.FirstOrDefault()
                : tbs.FirstOrDefault();

            if (titleBlock == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No title block found");

            var results = new List<object>();
            using var tx = new Transaction(doc, "RevitCortex: Workflow Sheet Set");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var sd in sheets)
            {
                var number = sd["number"]?.Value<string>();
                var name = sd["name"]?.Value<string>();

                try
                {
                    var sheet = ViewSheet.Create(doc, titleBlock.Id);
                    if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
                    if (!string.IsNullOrEmpty(name)) sheet.Name = name;
                    results.Add(new { sheetId = ToolHelpers.GetElementIdValue(sheet.Id), number = sheet.SheetNumber, name = sheet.Name, success = true });
                }
                catch (Exception ex)
                {
                    results.Add(new { number, name, success = false, reason = ex.Message });
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                createdCount = results.Count(r => ((dynamic)r).success),
                titleBlock = titleBlock.Name,
                sheets = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
