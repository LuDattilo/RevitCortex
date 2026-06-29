using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Performs UI operations on elements: select, selectionbox, setcolor, settransparency,
/// hide, temphide, isolate, unhide, resetisolate, delete.
/// Input uses a "data" wrapper to match the fork's OperateElementEventHandler schema.
/// </summary>
[ToolSafety(false, true)]
public class OperateElementTool : ICortexTool
{
    public string Name => "operate_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Performs UI operations on elements: select, selectionbox, setcolor, settransparency, hide, temphide, isolate, unhide, resetisolate, delete. Input uses a \"data\" wrapper to match the fork's OperateElementEventHandler schema.";
    // Supported action names (lowercase canonical form)
    private static readonly HashSet<string> KnownActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "selectionbox", "setcolor", "settransparency",
        "hide", "temphide", "isolate", "unhide", "resetisolate", "delete"
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // The fork wraps parameters in a "data" object — support both layouts
        var data = input["data"] as JObject ?? input;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // Parse action
        var action = data["action"]?.ToString();
        if (string.IsNullOrWhiteSpace(action))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "action is required",
                suggestion: $"Supported actions: {string.Join(", ", KnownActions)}");

        if (!KnownActions.Contains(action!))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unsupported action: '{action}'",
                suggestion: $"Supported actions: {string.Join(", ", KnownActions)}");

        // Parse elementIds (not required for resetisolate)
        var elementIdsToken = data["elementIds"];
        long[] rawIds = Array.Empty<long>();
        if (elementIdsToken != null && elementIdsToken.Type != JTokenType.Null)
        {
            try { rawIds = elementIdsToken.ToObject<long[]>() ?? Array.Empty<long>(); }
            catch { return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds must be an array of numbers"); }
        }

        bool isResetIsolate = string.Equals(action, "resetisolate", StringComparison.OrdinalIgnoreCase);
        if (rawIds.Length == 0 && !isResetIsolate)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required for this action (use an array of element ID numbers)");

        // Build ElementId collection
#if REVIT2024_OR_GREATER
        ICollection<ElementId> elementIds = rawIds.Select(id => new ElementId(id)).ToList();
#else
        ICollection<ElementId> elementIds = rawIds.Select(id => new ElementId((int)id)).ToList();
#endif

        // H31: 'delete' is destructive — validate IDs and confirm before dispatching.
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            // Drop IDs that no longer resolve to an element, so doc.Delete never
            // throws on a stale/invalid ID and the caller sees an accurate count.
            elementIds = elementIds.Where(id => doc.GetElement(id) != null).ToList();
            if (elementIds.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "None of the supplied elementIds exist in the active document");

            if (!session.RequestConfirmation("delete", elementIds.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                    "Operation cancelled by user");
        }

        // UIDocument for UI operations
        var uiDoc = new UIDocument(doc);

        try
        {
            string resultMessage = ExecuteAction(doc, uiDoc, action!, elementIds, data);
            return CortexResult<object>.Ok(new
            {
                message     = resultMessage,
                action,
                elementCount = elementIds.Count
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Operation '{action}' failed: {ex.Message}");
        }
    }

    // ── Action dispatcher ──────────────────────────────────────────────────

    private static string ExecuteAction(
        Document doc, UIDocument uiDoc,
        string action, ICollection<ElementId> elementIds,
        JObject data)
    {
        switch (action.ToLowerInvariant())
        {
            case "select":
                // No transaction needed — selection is a UI state
                uiDoc.Selection.SetElementIds(elementIds);
                return $"Selected {uiDoc.Selection.GetElementIds().Count} element(s)";

            case "selectionbox":
                return DoSelectionBox(doc, uiDoc, elementIds);

            case "setcolor":
                var colorToken = data["colorValue"];
                int[] colorValue = ParseColorArray(colorToken);
                using (var tx = new Transaction(doc, "RevitCortex: Set Element Color"))
                {
                    tx.Start();
                    SetElementsColor(doc, elementIds, colorValue);
                    tx.Commit();
                }
                uiDoc.ShowElements(elementIds);
                return $"Set color on {elementIds.Count} element(s)";

            case "settransparency":
                var transparencyToken = data["transparencyValue"];
                int transparency = Math.Max(0, Math.Min(100, transparencyToken?.Value<int>() ?? 50));
                using (var tx = new Transaction(doc, "RevitCortex: Set Element Transparency"))
                {
                    tx.Start();
                    var overrideSettings = new OverrideGraphicSettings();
                    overrideSettings.SetSurfaceTransparency(transparency);
                    foreach (var id in elementIds)
                        doc.ActiveView.SetElementOverrides(id, overrideSettings);
                    tx.Commit();
                }
                return $"Set transparency to {transparency}% on {elementIds.Count} element(s)";

            case "hide":
                using (var tx = new Transaction(doc, "RevitCortex: Hide Elements"))
                {
                    tx.Start();
                    doc.ActiveView.HideElements(elementIds);
                    tx.Commit();
                }
                return $"Hidden {elementIds.Count} element(s)";

            case "temphide":
                using (var tx = new Transaction(doc, "RevitCortex: Temp Hide Elements"))
                {
                    tx.Start();
                    doc.ActiveView.HideElementsTemporary(elementIds);
                    tx.Commit();
                }
                return $"Temporarily hidden {elementIds.Count} element(s)";

            case "isolate":
                using (var tx = new Transaction(doc, "RevitCortex: Isolate Elements"))
                {
                    tx.Start();
                    doc.ActiveView.IsolateElementsTemporary(elementIds);
                    tx.Commit();
                }
                return $"Isolated {elementIds.Count} element(s)";

            case "unhide":
                using (var tx = new Transaction(doc, "RevitCortex: Unhide Elements"))
                {
                    tx.Start();
                    doc.ActiveView.UnhideElements(elementIds);
                    tx.Commit();
                }
                return $"Unhidden {elementIds.Count} element(s)";

            case "resetisolate":
                using (var tx = new Transaction(doc, "RevitCortex: Reset Isolation"))
                {
                    tx.Start();
                    doc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    tx.Commit();
                }
                return "Isolation reset on active view";

            case "delete":
                using (var tx = new Transaction(doc, "RevitCortex: Delete Elements"))
                {
                    tx.Start();
                    try
                    {
                        var deleted = doc.Delete(elementIds);
                        tx.Commit();
                        var dependent = deleted.Count - elementIds.Count;
                        return dependent > 0
                            ? $"Deleted {deleted.Count} element(s) ({elementIds.Count} requested + {dependent} dependent)"
                            : $"Deleted {deleted.Count} element(s)";
                    }
                    catch
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();
                        throw;
                    }
                }

            default:
                throw new InvalidOperationException($"Unhandled action: {action}");
        }
    }

    // ── SelectionBox ───────────────────────────────────────────────────────

    private static string DoSelectionBox(Document doc, UIDocument uiDoc, ICollection<ElementId> elementIds)
    {
        // Find or switch to a 3D view
        View3D? targetView;
        if (doc.ActiveView is View3D v3d)
        {
            targetView = v3d;
        }
        else
        {
            targetView = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsLocked &&
                    (v.Name.Contains("{3D}") || v.Name.Contains("Default 3D")));

            if (targetView == null)
                throw new InvalidOperationException(
                    "Cannot find a suitable 3D view for creating section box. " +
                    "Open a 3D view first.");

            uiDoc.ActiveView = targetView;
        }

        // Calculate aggregate bounding box of all elements
        BoundingBoxXYZ? boundingBox = null;
        foreach (var id in elementIds)
        {
            var elem = doc.GetElement(id);
            var bb = elem?.get_BoundingBox(null);
            if (bb == null) continue;

            if (boundingBox == null)
            {
                boundingBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    Max = new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
                };
            }
            else
            {
                boundingBox.Min = new XYZ(
                    Math.Min(boundingBox.Min.X, bb.Min.X),
                    Math.Min(boundingBox.Min.Y, bb.Min.Y),
                    Math.Min(boundingBox.Min.Z, bb.Min.Z));
                boundingBox.Max = new XYZ(
                    Math.Max(boundingBox.Max.X, bb.Max.X),
                    Math.Max(boundingBox.Max.Y, bb.Max.Y),
                    Math.Max(boundingBox.Max.Z, bb.Max.Z));
            }
        }

        if (boundingBox == null)
            throw new InvalidOperationException(
                "Cannot create bounding box — no valid geometry found for the specified elements");

        // Expand by 1 foot offset
        const double offset = 1.0;
        boundingBox.Min = new XYZ(boundingBox.Min.X - offset, boundingBox.Min.Y - offset, boundingBox.Min.Z - offset);
        boundingBox.Max = new XYZ(boundingBox.Max.X + offset, boundingBox.Max.Y + offset, boundingBox.Max.Z + offset);

        using (var tx = new Transaction(doc, "RevitCortex: Create Section Box"))
        {
            tx.Start();
            targetView.IsSectionBoxActive = true;
            targetView.SetSectionBox(boundingBox);
            tx.Commit();
        }

        uiDoc.ShowElements(elementIds);
        return $"Section box created for {elementIds.Count} element(s) in view '{targetView.Name}'";
    }

    // ── SetColor ───────────────────────────────────────────────────────────

    private static void SetElementsColor(Document doc, ICollection<ElementId> elementIds, int[] colorValue)
    {
        int r = Math.Max(0, Math.Min(255, colorValue[0]));
        int g = Math.Max(0, Math.Min(255, colorValue[1]));
        int b = Math.Max(0, Math.Min(255, colorValue[2]));

        var color = new Color((byte)r, (byte)g, (byte)b);

        var overrideSettings = new OverrideGraphicSettings();
        overrideSettings.SetProjectionLineColor(color);
        overrideSettings.SetCutLineColor(color);
        overrideSettings.SetSurfaceForegroundPatternColor(color);
        overrideSettings.SetSurfaceBackgroundPatternColor(color);

        // Find a solid fill pattern and apply it so the color is visible on surfaces
        var solidPattern = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);

        if (solidPattern != null)
        {
            overrideSettings.SetSurfaceForegroundPatternId(solidPattern.Id);
            overrideSettings.SetSurfaceForegroundPatternVisible(true);
        }

        foreach (var id in elementIds)
            doc.ActiveView.SetElementOverrides(id, overrideSettings);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static int[] ParseColorArray(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return new[] { 255, 0, 0 }; // Default red

        try
        {
            var arr = token.ToObject<int[]>();
            if (arr != null && arr.Length >= 3)
                return arr;
        }
        catch { /* fall through to default */ }

        return new[] { 255, 0, 0 };
    }
}
