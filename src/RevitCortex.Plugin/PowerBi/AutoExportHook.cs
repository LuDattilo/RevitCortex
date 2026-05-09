using System;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Newtonsoft.Json.Linq;
using RevitCortex.Plugin.Communication;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Optional integration: when enabled, re-runs the Power BI export every time
/// the user saves the model. The export goes through the same socket call
/// as the wizard, so it respects all server-side settings (read-only mode,
/// audit log, etc.).
/// </summary>
public static class AutoExportHook
{
    private static PowerBiExportProfile? _profile;
    private static bool _attached;
    private static EventHandler<DocumentSavedEventArgs>? _handler;

    public static void Enable(PowerBiExportProfile profile)
    {
        _profile = profile;
        var ui = RevitCortexApp.Instance?.UiApplication;
        if (ui == null) return;

        if (!_attached)
        {
            _handler = OnDocumentSaved;
            ui.Application.DocumentSaved += _handler;
            _attached = true;
        }
    }

    public static void Disable()
    {
        var ui = RevitCortexApp.Instance?.UiApplication;
        if (ui != null && _attached && _handler != null)
        {
            ui.Application.DocumentSaved -= _handler;
        }
        _attached = false;
        _profile = null;
    }

    private static async void OnDocumentSaved(object? sender, DocumentSavedEventArgs e)
    {
        if (_profile == null) return;
        try
        {
            var port = RevitCortexApp.Instance?.Port ?? 8080;
            if (RevitCortexApp.Instance?.IsServiceRunning != true) return;

            var input = new JObject
            {
                ["categories"] = new JArray(_profile.Categories),
                ["includeTypeParameters"] = _profile.IncludeTypeParameters,
                ["maxElements"] = _profile.MaxElements
            };
            if (_profile.InstanceParameters.Count > 0 || _profile.TypeParameters.Count > 0)
            {
                var allParams = new System.Collections.Generic.List<string>(_profile.InstanceParameters);
                allParams.AddRange(_profile.TypeParameters);
                input["parameterNames"] = new JArray(allParams);
            }
            if (!string.IsNullOrEmpty(_profile.OutputFolder)) input["outputFolder"] = _profile.OutputFolder;
            if (!string.IsNullOrEmpty(_profile.FileName))
                input["fileName"] = _profile.OverwriteFile
                    ? _profile.FileName
                    : InsertTimestamp(_profile.FileName!);

            await RevitCortexJsonRpcClient.InvokeAsync(port, "push_to_powerbi", input);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Auto-export failed: {ex.Message}");
        }
    }

    private static string InsertTimestamp(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName);
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        return $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
    }
}
