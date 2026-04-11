using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Lists all linked file types with their instances, load status, path, and transform data.
/// Groups instances by their parent RevitLinkType.
/// </summary>
public class GetLinkedFileInstancesTool : ICortexTool
{
    public string Name => "get_linked_file_instances";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Lists all linked Revit files grouped by type, with instance transforms, load status, and file paths.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var linkNameFilter = input["linkName"]?.Value<string>();

        try
        {
            // Get all RevitLinkType elements
            var linkTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .ToList();

            // Get all RevitLinkInstance elements
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            // Group instances by type
            var instancesByType = linkInstances
                .GroupBy(li => li.GetTypeId())
                .ToDictionary(g => g.Key, g => g.ToList());

            var results = new List<object>();

            foreach (var linkType in linkTypes)
            {
                var typeName = linkType.Name;

                // Optional name filter
                if (!string.IsNullOrWhiteSpace(linkNameFilter) &&
                    typeName.IndexOf(linkNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var isLoaded = RevitLinkType.IsLoaded(doc, linkType.Id);
                var path = "";
                try
                {
                    var extRef = linkType.GetExternalFileReference();
                    if (extRef != null)
                    {
                        var modelPath = extRef.GetAbsolutePath();
                        path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                    }
                }
                catch { /* path unavailable */ }

                var instances = new List<object>();
                if (instancesByType.TryGetValue(linkType.Id, out var typeInstances))
                {
                    foreach (var inst in typeInstances)
                    {
                        var transform = inst.GetTotalTransform();
                        instances.Add(new
                        {
                            instanceId = GetIdLong(inst.Id),
                            isPinned = inst.Pinned,
                            origin = new { x = Math.Round(transform.Origin.X * MmPerFoot, 1), y = Math.Round(transform.Origin.Y * MmPerFoot, 1), z = Math.Round(transform.Origin.Z * MmPerFoot, 1) },
                            basisX = new { x = Math.Round(transform.BasisX.X, 6), y = Math.Round(transform.BasisX.Y, 6), z = Math.Round(transform.BasisX.Z, 6) },
                            basisY = new { x = Math.Round(transform.BasisY.X, 6), y = Math.Round(transform.BasisY.Y, 6), z = Math.Round(transform.BasisY.Z, 6) }
                        });
                    }
                }

                results.Add(new
                {
                    typeId = GetIdLong(linkType.Id),
                    typeName,
                    isLoaded,
                    path,
                    instanceCount = instances.Count,
                    instances
                });
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Found {results.Count} linked file type(s) with {linkInstances.Count} instance(s)",
                typeCount = results.Count,
                totalInstances = linkInstances.Count,
                linkedFiles = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private const double MmPerFoot = 304.8;

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
