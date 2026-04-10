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
/// Duplicates a sheet including all annotations and detail items.
/// Views can optionally be duplicated with detailing.
/// </summary>
public class DuplicateSheetWithContentTool : ICortexTool
{
    public string Name => "duplicate_sheet_with_content";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates a sheet including all annotations and detail items. Views can optionally be duplicated with detailing.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetId = input["sheetId"]?.Value<long>() ?? 0;
        var copies = input["copies"]?.Value<int>() ?? 1;
        var duplicateViews = input["duplicateViews"]?.Value<bool>() ?? true;
        var keepLegends = input["keepLegends"]?.Value<bool>() ?? true;
        var keepSchedules = input["keepSchedules"]?.Value<bool>() ?? true;
        var copyRevisions = input["copyRevisions"]?.Value<bool>() ?? false;
        var sheetNumberPrefix = input["sheetNumberPrefix"]?.Value<string>() ?? "";
        var sheetNumberSuffix = input["sheetNumberSuffix"]?.Value<string>() ?? "";

        if (sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetId is required");

        try
        {
#if REVIT2024_OR_GREATER
            var sourceSheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
#else
            var sourceSheet = doc.GetElement(new ElementId((int)sheetId)) as ViewSheet;
#endif
            if (sourceSheet == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Sheet not found");

            // Get title block type
            var titleBlockId = GetTitleBlockTypeId(doc, sourceSheet);

            // Get viewports and their positions
            var viewportData = new FilteredElementCollector(doc, sourceSheet.Id)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => new
                {
                    ViewId = vp.ViewId,
                    Center = vp.GetBoxCenter(),
                    View = doc.GetElement(vp.ViewId) as View
                })
                .Where(vp => vp.View != null)
                .ToList();

            // Get schedule instances
            var scheduleInstances = new FilteredElementCollector(doc, sourceSheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .Select(si => new { ScheduleId = si.ScheduleId, Point = si.Point })
                .ToList();

            var results = new List<object>();

            using var tx = new Transaction(doc, "RevitCortex: Duplicate Sheet With Content");
            tx.Start();

            for (int i = 0; i < copies; i++)
            {
                var newSheet = ViewSheet.Create(doc, titleBlockId);
                var suffix = copies > 1 ? $"-{i + 1:D2}" : "";
                newSheet.SheetNumber = $"{sheetNumberPrefix}{sourceSheet.SheetNumber}{sheetNumberSuffix}{suffix}";
                try { newSheet.Name = sourceSheet.Name; } catch { /* duplicate name */ }

                if (copyRevisions)
                {
                    var revIds = sourceSheet.GetAllRevisionIds();
                    if (revIds.Count > 0)
                        newSheet.SetAdditionalRevisionIds(revIds);
                }

                int viewportCount = 0;
                foreach (var vpData in viewportData)
                {
                    var view = vpData.View!;
                    var isLegend = view.ViewType == ViewType.Legend;
                    var isSchedule = view.ViewType == ViewType.Schedule;

                    if (isLegend && !keepLegends) continue;
                    if (isSchedule && !keepSchedules) continue;

                    if (isLegend || !duplicateViews)
                    {
                        if (Viewport.CanAddViewToSheet(doc, newSheet.Id, vpData.ViewId))
                        {
                            Viewport.Create(doc, newSheet.Id, vpData.ViewId, vpData.Center);
                            viewportCount++;
                        }
                    }
                    else
                    {
                        var newViewId = view.Duplicate(ViewDuplicateOption.WithDetailing);
                        var newView = doc.GetElement(newViewId) as View;
                        if (newView != null)
                        {
                            try { newView.Name = $"{view.Name} - {newSheet.SheetNumber}"; } catch { }
                            Viewport.Create(doc, newSheet.Id, newViewId, vpData.Center);
                            viewportCount++;
                        }
                    }
                }

                if (keepSchedules)
                {
                    foreach (var si in scheduleInstances)
                    {
                        ScheduleSheetInstance.Create(doc, newSheet.Id, si.ScheduleId, si.Point);
                    }
                }

                results.Add(new
                {
                    sheetId = GetIdLong(newSheet.Id),
                    number = newSheet.SheetNumber,
                    name = newSheet.Name,
                    viewportCount
                });
            }

            tx.Commit();
            return CortexResult<object>.Ok(new { duplicatedCount = results.Count, sheets = results });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static ElementId GetTitleBlockTypeId(Document doc, ViewSheet sheet)
    {
        var tb = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .FirstOrDefault();
        if (tb != null) return tb.GetTypeId();

        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
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
