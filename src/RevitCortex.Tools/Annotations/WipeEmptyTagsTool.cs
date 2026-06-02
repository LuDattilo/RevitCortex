using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Finds and removes tags that have empty text or reference deleted/invalid elements.
/// </summary>
public class WipeEmptyTagsTool : ICortexTool
{
    public string Name => "wipe_empty_tags";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Finds and removes tags that have empty text or reference deleted/invalid elements.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var viewId = input["viewId"]?.Value<long>();
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();

        try
        {
            FilteredElementCollector collector;
            if (viewId.HasValue && viewId.Value > 0)
            {
#if REVIT2024_OR_GREATER
                collector = new FilteredElementCollector(doc, new ElementId(viewId.Value));
#else
                collector = new FilteredElementCollector(doc, new ElementId((int)viewId.Value));
#endif
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }

            var tags = collector
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (categories.Count > 0)
            {
                var catIds = categories
                    .Select(c => Utilities.CategoryResolver.ResolveToId(doc, c))
                    .Where(id => id != ElementId.InvalidElementId)
                    .ToHashSet();
                tags = tags.Where(t => t.Category != null && catIds.Contains(t.Category.Id)).ToList();
            }

            var emptyTags = new List<object>();
            foreach (var tag in tags)
            {
                bool isEmpty = false;
                string reason = "";

                try
                {
                    // Check if tag references a valid element
#if REVIT2024_OR_GREATER
                    var taggedIds = tag.GetTaggedElementIds();
                    if (!taggedIds.Any())
                    {
                        isEmpty = true;
                        reason = "No tagged element";
                    }
                    else
                    {
                        foreach (var linkedElemId in taggedIds)
                        {
                            var elem = doc.GetElement(linkedElemId.HostElementId);
                            if (elem == null)
                            {
                                isEmpty = true;
                                reason = "Tagged element deleted";
                                break;
                            }
                        }
                    }
#else
                    var taggedElems = tag.GetTaggedLocalElements();
                    if (taggedElems == null || taggedElems.Count == 0)
                    {
                        isEmpty = true;
                        reason = "Tagged element not found";
                    }
#endif

                    // Check if tag text is empty
                    if (!isEmpty)
                    {
                        var tagText = tag.TagText;
                        if (string.IsNullOrWhiteSpace(tagText))
                        {
                            isEmpty = true;
                            reason = "Empty tag text";
                        }
                    }
                }
                catch
                {
                    isEmpty = true;
                    reason = "Error reading tag";
                }

                if (isEmpty)
                    emptyTags.Add(new { id = ToolHelpers.GetElementIdValue(tag.Id), reason, viewName = tag.OwnerViewId != ElementId.InvalidElementId ? doc.GetElement(tag.OwnerViewId)?.Name : null });
            }

            if (!dryRun && emptyTags.Count > 0)
            {
                if (!session.RequestConfirmation("delete empty tags from", emptyTags.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Wipe Empty Tags");
                tx.Start();
                int deleted = 0;
                // Surface per-tag delete failures (e.g. a tag pinned or in a locked workset)
                // instead of swallowing them: deletedCount alone hides partial failure.
                var failures = new List<object>();
                foreach (dynamic t in emptyTags)
                {
                    try
                    {
#if REVIT2024_OR_GREATER
                        doc.Delete(new ElementId((long)t.id));
#else
                        doc.Delete(new ElementId((int)t.id));
#endif
                        deleted++;
                    }
                    catch (Exception ex) { failures.Add(new { id = t.id, reason = ex.Message }); }
                }
                tx.Commit();
                return CortexResult<object>.Ok(new
                {
                    dryRun = false,
                    deletedCount = deleted,
                    emptyTagCount = emptyTags.Count,
                    failedCount = failures.Count,
                    failures = failures.Take(50).ToList()
                });
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                emptyTagCount = emptyTags.Count,
                emptyTags = emptyTags.Take(200).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
