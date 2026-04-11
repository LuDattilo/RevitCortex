using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Sheets;

/// <summary>
/// Creates multiple sheets at once with title blocks and optional view placement.
/// </summary>
public class BatchCreateSheetsTool : ICortexTool
{
    public string Name => "batch_create_sheets";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates multiple sheets at once with title blocks and optional view placement.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetsArray = input["sheets"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var defaultTitleBlockName = input["defaultTitleBlockName"]?.Value<string>();

        if (sheetsArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheets array is required");

        try
        {
            // Resolve default title block
            var defaultTbId = ResolveTitleBlock(doc, defaultTitleBlockName);

            var results = new List<object>();
            using var tx = new Transaction(doc, "RevitCortex: Batch Create Sheets");
            tx.Start();

            foreach (var sheetDef in sheetsArray)
            {
                var number = sheetDef["number"]?.Value<string>();
                var name = sheetDef["name"]?.Value<string>();
                var tbName = sheetDef["titleBlockName"]?.Value<string>();
                var viewIds = sheetDef["viewIds"]?.ToObject<List<long>>() ?? new List<long>();

                var tbId = !string.IsNullOrEmpty(tbName) ? ResolveTitleBlock(doc, tbName) : defaultTbId;
                if (tbId == ElementId.InvalidElementId)
                {
                    results.Add(new { number, name, success = false, reason = "No title block found" });
                    continue;
                }

                try
                {
                    var sheet = ViewSheet.Create(doc, tbId);
                    if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
                    if (!string.IsNullOrEmpty(name)) sheet.Name = name;

                    var placedViews = new List<object>();
                    foreach (var vid in viewIds)
                    {
#if REVIT2024_OR_GREATER
                        var viewEid = new ElementId(vid);
#else
                        var viewEid = new ElementId((int)vid);
#endif
                        if (Viewport.CanAddViewToSheet(doc, sheet.Id, viewEid))
                        {
                            var vp = Viewport.Create(doc, sheet.Id, viewEid, new XYZ(0.5, 0.5, 0));
                            placedViews.Add(new { viewId = vid, viewportId = ToolHelpers.GetElementIdValue(vp.Id), success = true });
                        }
                        else
                        {
                            placedViews.Add(new { viewId = vid, viewportId = (long?)null, success = false });
                        }
                    }

                    results.Add(new
                    {
                        sheetId = ToolHelpers.GetElementIdValue(sheet.Id),
                        number = sheet.SheetNumber,
                        name = sheet.Name,
                        success = true,
                        placedViews
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { number, name, success = false, reason = ex.Message });
                }
            }

            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                createdCount = results.Count(r => ((dynamic)r).success),
                sheets = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static ElementId ResolveTitleBlock(Document doc, string? name)
    {
        var tbs = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>();

        if (!string.IsNullOrEmpty(name))
        {
            var match = tbs.FirstOrDefault(tb =>
                tb.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                tb.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                $"{tb.FamilyName}: {tb.Name}".Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
        }

        var first = tbs.FirstOrDefault();
        return first?.Id ?? ElementId.InvalidElementId;
    }
}
