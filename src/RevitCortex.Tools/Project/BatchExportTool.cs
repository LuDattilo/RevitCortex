using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Exports multiple views/sheets to DWG, DXF, DGN, or image formats.
/// PDF export requires Revit 2023+ PDF export API.
/// </summary>
[ToolSafety(true, false)]
public class BatchExportTool : ICortexTool
{
    public string Name => "batch_export";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports multiple views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var format = input["format"]?.Value<string>() ?? "DWG";
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var outputDir = input["outputDirectory"]?.Value<string>();

        if (sheetIds.Count == 0 && viewIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetIds or viewIds required");

        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RevitExport");
        }

        // H25-wave: this tool creates directories and writes export files — restrict the
        // target to user-owned directories; reject traversal/UNC/system paths.
        if (!Utilities.PathSafety.TryResolveSafe(outputDir, out var safeOutputDir, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        outputDir = safeOutputDir;

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        try
        {
            // Collect all view/sheet IDs
            var allIds = new List<ElementId>();
            foreach (var sid in sheetIds)
            {
#if REVIT2024_OR_GREATER
                allIds.Add(new ElementId(sid));
#else
                allIds.Add(new ElementId((int)sid));
#endif
            }
            foreach (var vid in viewIds)
            {
#if REVIT2024_OR_GREATER
                allIds.Add(new ElementId(vid));
#else
                allIds.Add(new ElementId((int)vid));
#endif
            }

            var results = new List<object>();

            switch (format.ToUpperInvariant())
            {
                case "DWG":
                {
                    var options = new DWGExportOptions();
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var viewSet = new List<ElementId> { id };
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            doc.Export(outputDir, name, viewSet, options);
                            results.Add(new { name = view.Name, file = $"{name}.dwg", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "DXF":
                {
                    var options = new DXFExportOptions();
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var viewSet = new List<ElementId> { id };
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            doc.Export(outputDir, name, viewSet, options);
                            results.Add(new { name = view.Name, file = $"{name}.dxf", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "DGN":
                {
                    var options = new DGNExportOptions();
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var viewSet = new List<ElementId> { id };
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            doc.Export(outputDir, name, viewSet, options);
                            results.Add(new { name = view.Name, file = $"{name}.dgn", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "IMAGE":
                {
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            var imgOptions = new ImageExportOptions
                            {
                                FilePath = Path.Combine(outputDir, $"{name}.png"),
                                ExportRange = ExportRange.SetOfViews,
                                ZoomType = ZoomFitType.FitToPage,
                                ImageResolution = ImageResolution.DPI_300,
                                HLRandWFViewsFileType = ImageFileType.PNG
                            };
                            imgOptions.SetViewsAndSheets(new List<ElementId> { id });
                            doc.ExportImage(imgOptions);
                            results.Add(new { name = view.Name, file = $"{name}.png", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "PDF":
                {
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            var pdfOptions = new PDFExportOptions
                            {
                                FileName = name,
                                Combine = false
                            };
                            doc.Export(outputDir, new List<ElementId> { id }, pdfOptions);
                            results.Add(new { name = view.Name, file = $"{name}.pdf", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unsupported format: {format}", suggestion: "Use: DWG, DXF, DGN, PDF, IMAGE");
            }

            return CortexResult<object>.Ok(new
            {
                format,
                outputDirectory = outputDir,
                exportedCount = results.Count(r => ((dynamic)r).success),
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
