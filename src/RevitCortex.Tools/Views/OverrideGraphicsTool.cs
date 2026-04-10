using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Sets or resets graphic overrides (color, transparency, halftone, line weight) for elements in a view.
/// </summary>
public class OverrideGraphicsTool : ICortexTool
{
    public string Name => "override_graphics";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Sets or resets graphic overrides (color, transparency, halftone, line weight) for elements in a view.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (elementIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds array is required");

        var action = input["action"]?.Value<string>() ?? "set";
        var viewIdLong = input["viewId"]?.Value<long>() ?? 0;

        try
        {
            View? view;
            if (viewIdLong > 0)
            {
#if REVIT2024_OR_GREATER
                view = doc.GetElement(new ElementId(viewIdLong)) as View;
#else
                view = doc.GetElement(new ElementId((int)viewIdLong)) as View;
#endif
            }
            else view = doc.ActiveView;

            if (view == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Could not resolve view");

            if (!session.RequestConfirmation("modify graphics for", elementIds.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            using var tx = new Transaction(doc, "RevitCortex: Override Graphics");
            tx.Start();

            int modified = 0;
            if (action == "reset")
            {
                var emptyOverrides = new OverrideGraphicSettings();
                foreach (var eid in elementIds)
                {
#if REVIT2024_OR_GREATER
                    view.SetElementOverrides(new ElementId(eid), emptyOverrides);
#else
                    view.SetElementOverrides(new ElementId((int)eid), emptyOverrides);
#endif
                    modified++;
                }
            }
            else
            {
                var overrides = new OverrideGraphicSettings();

                var r = input["colorR"]?.Value<int?>();
                var g = input["colorG"]?.Value<int?>();
                var b = input["colorB"]?.Value<int?>();
                if (r.HasValue && g.HasValue && b.HasValue)
                {
                    var color = new Color((byte)r.Value, (byte)g.Value, (byte)b.Value);
                    overrides.SetProjectionLineColor(color);
                    overrides.SetSurfaceForegroundPatternColor(color);

                    var solidFill = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                    if (solidFill != null)
                        overrides.SetSurfaceForegroundPatternId(solidFill.Id);
                }

                var transparency = input["transparency"]?.Value<int?>();
                if (transparency.HasValue)
                    overrides.SetSurfaceTransparency(transparency.Value);

                var isHalftone = input["isHalftone"]?.Value<bool?>();
                if (isHalftone.HasValue)
                    overrides.SetHalftone(isHalftone.Value);

                var lineWeight = input["projectionLineWeight"]?.Value<int?>();
                if (lineWeight.HasValue)
                    overrides.SetProjectionLineWeight(lineWeight.Value);

                foreach (var eid in elementIds)
                {
#if REVIT2024_OR_GREATER
                    view.SetElementOverrides(new ElementId(eid), overrides);
#else
                    view.SetElementOverrides(new ElementId((int)eid), overrides);
#endif
                    modified++;
                }
            }

            tx.Commit();
            return CortexResult<object>.Ok(new { action, modifiedCount = modified, viewName = view.Name });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
