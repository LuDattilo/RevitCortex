using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Creates a new sheet with optional title block and numbering.
/// </summary>
public class CreateSheetTool : ICortexTool
{
    public string Name => "create_sheet";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetNumber = input["sheetNumber"]?.Value<string>();
        var sheetName = input["sheetName"]?.Value<string>();
        var titleBlockFamilyName = input["titleBlockFamilyName"]?.Value<string>();
        var titleBlockTypeName = input["titleBlockTypeName"]?.Value<string>();
        var titleBlockTypeId = input["titleBlockTypeId"]?.Value<long>() ?? -1;

        try
        {
            // Resolve title block
            ElementId tbId = ElementId.InvalidElementId;

            if (titleBlockTypeId > 0)
            {
#if REVIT2024_OR_GREATER
                var elem = doc.GetElement(new ElementId(titleBlockTypeId));
#else
                var elem = doc.GetElement(new ElementId((int)titleBlockTypeId));
#endif
                if (elem is FamilySymbol) tbId = elem.Id;
            }

            if (tbId == ElementId.InvalidElementId && !string.IsNullOrEmpty(titleBlockFamilyName))
            {
                var symbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>();

                FamilySymbol? match = null;
                if (!string.IsNullOrEmpty(titleBlockTypeName))
                    match = symbols.FirstOrDefault(s =>
                        s.FamilyName.Equals(titleBlockFamilyName, StringComparison.OrdinalIgnoreCase) &&
                        s.Name.Equals(titleBlockTypeName, StringComparison.OrdinalIgnoreCase));

                match ??= symbols.FirstOrDefault(s =>
                    s.FamilyName.Equals(titleBlockFamilyName, StringComparison.OrdinalIgnoreCase));

                if (match != null) tbId = match.Id;
            }

            if (tbId == ElementId.InvalidElementId)
            {
                var first = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .FirstOrDefault();
                if (first != null) tbId = first.Id;
            }

            using var tx = new Transaction(doc, "RevitCortex: Create Sheet");
            tx.Start();

            // Activate title block if needed
            if (tbId != ElementId.InvalidElementId)
            {
                var symbol = doc.GetElement(tbId) as FamilySymbol;
                if (symbol != null && !symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }
            }

            var sheet = ViewSheet.Create(doc, tbId);

            if (!string.IsNullOrEmpty(sheetNumber))
                sheet.SheetNumber = sheetNumber;
            if (!string.IsNullOrEmpty(sheetName))
                sheet.Name = sheetName;

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                sheetId = GetIdLong(sheet.Id),
                sheetNumber = sheet.SheetNumber,
                sheetName = sheet.Name
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create sheet: {ex.Message}");
        }
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
