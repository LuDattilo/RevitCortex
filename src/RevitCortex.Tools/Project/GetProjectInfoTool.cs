using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Returns comprehensive project metadata: name, address, author, phases,
/// worksets, Revit links, and levels.
/// </summary>
public class GetProjectInfoTool : ICortexTool
{
    public string Name => "get_project_info";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var includePhases   = input["includePhases"]?.Value<bool>() ?? true;
        var includeWorksets = input["includeWorksets"]?.Value<bool>() ?? true;
        var includeLinks    = input["includeLinks"]?.Value<bool>() ?? true;
        var includeLevels   = input["includeLevels"]?.Value<bool>() ?? true;

        try
        {
            var projectInfo = doc.ProjectInformation;
            var result = new Dictionary<string, object>
            {
                ["projectName"]              = projectInfo?.Name ?? "",
                ["projectNumber"]            = projectInfo?.Number ?? "",
                ["projectAddress"]           = projectInfo?.Address ?? "",
                ["buildingName"]             = projectInfo?.BuildingName ?? "",
                ["author"]                   = projectInfo?.Author ?? "",
                ["organizationName"]         = projectInfo?.OrganizationName ?? "",
                ["organizationDescription"]  = projectInfo?.OrganizationDescription ?? "",
                ["issueDate"]                = projectInfo?.IssueDate ?? "",
                ["status"]                   = projectInfo?.Status ?? "",
                ["filePath"]                 = doc.PathName ?? "",
                ["isWorkshared"]             = doc.IsWorkshared
            };

            if (includePhases)
            {
                var phases = new List<object>();
                foreach (Phase phase in doc.Phases)
                {
                    phases.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = phase.Id.Value,
#else
                        id = (long)phase.Id.IntegerValue,
#endif
                        name = phase.Name
                    });
                }
                result["phases"] = phases;
            }

            if (includeWorksets && doc.IsWorkshared)
            {
                var worksets = new List<object>();
                var wsCollector = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset);

                foreach (var ws in wsCollector)
                {
                    worksets.Add(new
                    {
                        id   = ws.Id.IntegerValue,
                        name = ws.Name,
                        isOpen     = ws.IsOpen,
                        isEditable = ws.IsEditable,
                        owner      = ws.Owner
                    });
                }
                result["worksets"] = worksets;
            }

            if (includeLinks)
            {
                var links = new List<object>();
                var linkCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance));

                foreach (RevitLinkInstance link in linkCollector)
                {
                    var linkType = doc.GetElement(link.GetTypeId()) as RevitLinkType;
                    links.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = link.Id.Value,
#else
                        id = (long)link.Id.IntegerValue,
#endif
                        name     = link.Name,
                        isLoaded = linkType != null && RevitLinkType.IsLoaded(doc, linkType.Id),
                        linkPath = GetLinkPath(linkType)
                    });
                }
                result["links"] = links;
            }

            if (includeLevels)
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new
                    {
#if REVIT2024_OR_GREATER
                        id = l.Id.Value,
#else
                        id = (long)l.Id.IntegerValue,
#endif
                        name      = l.Name,
                        elevation = Math.Round(l.Elevation * 304.8, 2) // feet → mm
                    })
                    .ToList();

                result["levels"] = levels;
            }

            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get project info: {ex.Message}");
        }
    }

    private static string GetLinkPath(RevitLinkType? linkType)
    {
        if (linkType == null) return "";
        try
        {
            var externalRef = linkType.GetExternalFileReference();
            if (externalRef == null) return "";
            var modelPath = externalRef.GetAbsolutePath();
            return ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
        }
        catch
        {
            return "";
        }
    }
}
