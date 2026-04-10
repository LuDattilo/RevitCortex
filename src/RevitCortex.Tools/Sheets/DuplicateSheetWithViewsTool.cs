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
/// Duplicates a sheet and its placed views with configurable duplication options.
/// Also copies title block parameters from source sheet.
/// </summary>
public class DuplicateSheetWithViewsTool : ICortexTool
{
    public string Name => "duplicate_sheet_with_views";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

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
        var newSheetNumberPrefix = input["newSheetNumberPrefix"]?.Value<string>() ?? "";
        var viewDuplicateOptionStr = input["viewDuplicateOption"]?.Value<string>() ?? "DuplicateWithDetailing";

        if (sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetId is required");

        var viewDupOption = viewDuplicateOptionStr switch
        {
            "Duplicate" => ViewDuplicateOption.Duplicate,
            "DuplicateAsDependent" => ViewDuplicateOption.AsDependent,
            _ => ViewDuplicateOption.WithDetailing
        };

        try
        {
#if REVIT2024_OR_GREATER
            var sourceSheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
#else
            var sourceSheet = doc.GetElement(new ElementId((int)sheetId)) as ViewSheet;
#endif
            if (sourceSheet == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Sheet not found");

            // Get title block type and instance
            var titleBlockInstance = new FilteredElementCollector(doc, sourceSheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .FirstOrDefault();
            var titleBlockTypeId = titleBlockInstance?.GetTypeId()
                ?? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .FirstOrDefault()?.Id
                ?? ElementId.InvalidElementId;

            // Get viewports
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

            using var tx = new Transaction(doc, "RevitCortex: Duplicate Sheet With Views");
            tx.Start();

            for (int i = 0; i < copies; i++)
            {
                var newSheet = ViewSheet.Create(doc, titleBlockTypeId);
                var suffix = copies > 1 ? $"-{i + 1:D2}" : "";
                newSheet.SheetNumber = $"{newSheetNumberPrefix}{sourceSheet.SheetNumber}{suffix}";
                try { newSheet.Name = sourceSheet.Name; } catch { }

                // Copy title block parameters
                if (titleBlockInstance != null)
                {
                    var newTb = new FilteredElementCollector(doc, newSheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .FirstOrDefault();
                    if (newTb != null)
                        CopyParameters(titleBlockInstance, newTb);
                }

                int viewportCount = 0;
                foreach (var vpData in viewportData)
                {
                    var view = vpData.View!;
                    var isLegend = view.ViewType == ViewType.Legend;

                    if (isLegend && !keepLegends) continue;

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
                        var newViewId = view.Duplicate(viewDupOption);
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

    private static void CopyParameters(Element source, Element target)
    {
        foreach (Parameter srcParam in source.Parameters)
        {
            if (srcParam.IsReadOnly) continue;
            var tgtParam = target.LookupParameter(srcParam.Definition.Name);
            if (tgtParam == null || tgtParam.IsReadOnly) continue;

            try
            {
                switch (srcParam.StorageType)
                {
                    case StorageType.String:
                        var s = srcParam.AsString();
                        if (s != null) tgtParam.Set(s);
                        break;
                    case StorageType.Integer:
                        tgtParam.Set(srcParam.AsInteger());
                        break;
                    case StorageType.Double:
                        tgtParam.Set(srcParam.AsDouble());
                        break;
                    case StorageType.ElementId:
                        tgtParam.Set(srcParam.AsElementId());
                        break;
                }
            }
            catch { /* skip unwritable params */ }
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
