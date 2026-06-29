using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Receives a list of element IDs from a Power BI drillthrough (via the
/// revitcortex:// protocol handler) and selects/zooms/isolates them in the
/// active Revit view. This is the bridge that closes the loop PBI → Revit.
/// </summary>
[ToolSafety(false, false)]
public class SelectFromPowerBiTool : ICortexTool
{
    public string Name => "select_from_powerbi";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Selects/zooms/isolates elements in Revit from a Power BI drillthrough action.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var idsToken = input["elementIds"];
        if (idsToken == null || idsToken.Type == JTokenType.Null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required (array of long)");

        long[] rawIds;
        try { rawIds = idsToken.ToObject<long[]>() ?? Array.Empty<long>(); }
        catch
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds must be an array of numbers");
        }

        if (rawIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is empty");

        var action = (input["action"]?.ToString() ?? "select").ToLowerInvariant();

#if REVIT2024_OR_GREATER
        ICollection<ElementId> elementIds = rawIds.Select(id => new ElementId(id)).ToList();
#else
        ICollection<ElementId> elementIds = rawIds.Select(id => new ElementId((int)id)).ToList();
#endif

        // Filter out invalid IDs (elements deleted between PBI snapshot and now)
        var validIds = elementIds.Where(id => doc.GetElement(id) != null).ToList();
        var missingCount = elementIds.Count - validIds.Count;
        if (validIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "None of the requested elements exist in the active document.",
                suggestion: "The Power BI dataset may be stale: re-run push_to_powerbi to refresh.");

        var uiDoc = new UIDocument(doc);
        try
        {
            switch (action)
            {
                case "highlight":
                case "select":
                    uiDoc.Selection.SetElementIds(validIds);
                    uiDoc.ShowElements(validIds);
                    BringRevitToFront();
                    return CortexResult<object>.Ok(new
                    {
                        message = $"Selected {validIds.Count} element(s)",
                        selectedCount = validIds.Count,
                        missingCount,
                        action
                    });

                case "isolate":
                    using (var tx = new Transaction(doc, "RevitCortex: Isolate from PBI"))
                    {
                        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                        tx.Start();
                        doc.ActiveView.IsolateElementsTemporary(validIds);
                        if (tx.Commit() != TransactionStatus.Committed)
                            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                                suggestion: "Fix the reported model errors and retry.");
                    }
                    uiDoc.Selection.SetElementIds(validIds);
                    uiDoc.ShowElements(validIds);
                    BringRevitToFront();
                    return CortexResult<object>.Ok(new
                    {
                        message = $"Isolated {validIds.Count} element(s)",
                        selectedCount = validIds.Count,
                        missingCount,
                        action
                    });

                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unsupported action '{action}'",
                        suggestion: "Use 'select', 'highlight', or 'isolate'.");
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"select_from_powerbi failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Brings the Revit main window to the foreground after a protocol-handler
    /// invocation, so the user actually sees the selection without alt-tabbing.
    /// </summary>
    private static void BringRevitToFront()
    {
        try
        {
            var hWnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (hWnd != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(hWnd);
        }
        catch
        {
            // Best effort
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
