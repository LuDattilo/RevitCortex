using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Sheets;

/// <summary>
/// Creates, lists, converts, or deletes placeholder sheets.
/// </summary>
public class CreatePlaceholderSheetsTool : ICortexTool
{
    public string Name => "create_placeholder_sheets";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates, lists, converts, or deletes placeholder sheets.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "create";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "create" => CreatePlaceholders(doc, input),
                "list" => ListPlaceholders(doc),
                "convert" => ConvertPlaceholders(doc, input),
                "delete" => DeletePlaceholders(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: create, list, convert, delete")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> CreatePlaceholders(Document doc, JObject input)
    {
        var sheetsArray = input["sheets"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        if (sheetsArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheets array is required for create");

        var results = new List<object>();
        using var tx = new Transaction(doc, "RevitCortex: Create Placeholder Sheets");
        tx.Start();

        foreach (var sd in sheetsArray)
        {
            var number = sd["number"]?.Value<string>();
            var name = sd["name"]?.Value<string>();

            var sheet = ViewSheet.CreatePlaceholder(doc);
            if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
            if (!string.IsNullOrEmpty(name)) sheet.Name = name;

            results.Add(new { sheetId = GetIdLong(sheet.Id), number = sheet.SheetNumber, name = sheet.Name });
        }

        tx.Commit();
        return CortexResult<object>.Ok(new { createdCount = results.Count, sheets = results });
    }

    private static CortexResult<object> ListPlaceholders(Document doc)
    {
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => s.IsPlaceholder)
            .Select(s => new { id = GetIdLong(s.Id), number = s.SheetNumber, name = s.Name })
            .ToList();

        return CortexResult<object>.Ok(new { placeholderCount = sheets.Count, sheets });
    }

    private static CortexResult<object> ConvertPlaceholders(Document doc, JObject input)
    {
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        var titleBlockId = input["titleBlockId"]?.Value<long>() ?? 0;

        if (sheetIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetIds required for convert");

        // Resolve title block
        ElementId tbId;
        if (titleBlockId > 0)
        {
#if REVIT2024_OR_GREATER
            tbId = new ElementId(titleBlockId);
#else
            tbId = new ElementId((int)titleBlockId);
#endif
        }
        else
        {
            var firstTb = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .FirstOrDefault();
            tbId = firstTb?.Id ?? ElementId.InvalidElementId;
        }

        if (tbId == ElementId.InvalidElementId)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No title block found");

        var results = new List<object>();
        using var tx = new Transaction(doc, "RevitCortex: Convert Placeholder Sheets");
        tx.Start();

        foreach (var sid in sheetIds)
        {
#if REVIT2024_OR_GREATER
            var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
#else
            var sheet = doc.GetElement(new ElementId((int)sid)) as ViewSheet;
#endif
            if (sheet == null || !sheet.IsPlaceholder)
            {
                results.Add(new { sheetId = sid, success = false, reason = "Not a placeholder sheet" });
                continue;
            }

            var savedNumber = sheet.SheetNumber;
            var savedName = sheet.Name;

            doc.Delete(sheet.Id);
            var newSheet = ViewSheet.Create(doc, tbId);
            newSheet.SheetNumber = savedNumber;
            newSheet.Name = savedName;

            results.Add(new
            {
                sheetId = GetIdLong(newSheet.Id),
                number = savedNumber,
                name = savedName,
                success = true
            });
        }

        tx.Commit();
        return CortexResult<object>.Ok(new { convertedCount = results.Count(r => ((dynamic)r).success), sheets = results });
    }

    private static CortexResult<object> DeletePlaceholders(Document doc, JObject input)
    {
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (sheetIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetIds required for delete");

        using var tx = new Transaction(doc, "RevitCortex: Delete Placeholder Sheets");
        tx.Start();
        int deleted = 0;
        foreach (var sid in sheetIds)
        {
#if REVIT2024_OR_GREATER
            var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
#else
            var sheet = doc.GetElement(new ElementId((int)sid)) as ViewSheet;
#endif
            if (sheet != null) { doc.Delete(sheet.Id); deleted++; }
        }
        tx.Commit();
        return CortexResult<object>.Ok(new { deletedCount = deleted });
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
