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
/// Lists, reloads, or unloads linked Revit/CAD/IFC files.
/// </summary>
public class ManageLinksTool : ICortexTool
{
    public string Name => "manage_links";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, reloads, or unloads linked Revit/CAD/IFC files.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";
        var linkId = input["linkId"]?.Value<long>() ?? 0;

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListLinks(doc),
                "reload" => ReloadLink(doc, linkId),
                "unload" => UnloadLink(doc, linkId),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, reload, unload")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListLinks(Document doc)
    {
        var links = new List<object>();

        // Revit links
        foreach (var rl in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var linkType = doc.GetElement(rl.GetTypeId()) as RevitLinkType;
            links.Add(new
            {
                id = GetIdLong(rl.Id),
                typeId = linkType != null ? GetIdLong(linkType.Id) : 0,
                name = rl.Name,
                linkType = "Revit",
                isLoaded = RevitLinkType.IsLoaded(doc, rl.GetTypeId()),
                path = linkType?.GetExternalFileReference()?.GetAbsolutePath()?.ToString() ?? ""
            });
        }

        // CAD links
        foreach (var import in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
        {
            links.Add(new
            {
                id = GetIdLong(import.Id),
                typeId = (long)0,
                name = import.Name,
                linkType = import.IsLinked ? "CAD_Link" : "CAD_Import",
                isLoaded = true,
                path = ""
            });
        }

        return CortexResult<object>.Ok(new { linkCount = links.Count, links });
    }

    private static CortexResult<object> ReloadLink(Document doc, long linkId)
    {
        if (linkId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "linkId required for reload");

#if REVIT2024_OR_GREATER
        var linkInstance = doc.GetElement(new ElementId(linkId)) as RevitLinkInstance;
#else
        var linkInstance = doc.GetElement(new ElementId((int)linkId)) as RevitLinkInstance;
#endif
        if (linkInstance == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Link not found");

        var linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
        if (linkType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Link type not found");

        using var tx = new Transaction(doc, "RevitCortex: Reload Link");
        tx.Start();
        linkType.Reload();
        tx.Commit();

        return CortexResult<object>.Ok(new { linkId, name = linkInstance.Name, action = "reloaded" });
    }

    private static CortexResult<object> UnloadLink(Document doc, long linkId)
    {
        if (linkId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "linkId required for unload");

#if REVIT2024_OR_GREATER
        var linkInstance = doc.GetElement(new ElementId(linkId)) as RevitLinkInstance;
#else
        var linkInstance = doc.GetElement(new ElementId((int)linkId)) as RevitLinkInstance;
#endif
        if (linkInstance == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Link not found");

        var linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
        if (linkType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Link type not found");

        using var tx = new Transaction(doc, "RevitCortex: Unload Link");
        tx.Start();
        linkType.Unload(null);
        tx.Commit();

        return CortexResult<object>.Ok(new { linkId, name = linkInstance.Name, action = "unloaded" });
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
