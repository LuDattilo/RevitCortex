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

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Selects and frames a mixed host/link clash context: normal host elements
/// plus sub-elements inside Revit links. Because Revit cannot reliably
/// isolate a single sub-element inside a link, this tool also creates a
/// DirectShape marker in the host document around each linked target's
/// bounding box so the user can actually see where the linked element is.
/// </summary>
public class ShowCrossModelElementsTool : ICortexTool
{
    public string Name => "show_cross_model_elements";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Select host elements plus elements in linked Revit models. Two strategies for visibility: (a) default — create red DirectShape markers in the host doc around each linked element's bounding box (synchronous, transactional, robust); (b) usePostCommandIsolate=true — use Revit's native IsolateElements via PostCommand after SetReferences (canonical Revit API pattern, but asynchronous: tool returns before isolate completes, and cannot be combined with section box / overrides in the same call).";

    private const double MmPerFoot = 304.8;
    private const string MarkerCommentTag = "RevitCortex:CrossModelMarker";
    private const double MarkerOffsetMm = 50.0;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var hostIds = ReadLongArray(input["hostElementIds"]);
        var linkedTargets = input["linkedElements"] as JArray ?? new JArray();
        var shouldSelect = input["select"]?.Value<bool>() ?? true;
        var shouldIsolate = input["isolate"]?.Value<bool>() ?? true;
        var createSectionBox = input["createSectionBox"]?.Value<bool>() ?? true;
        var createLinkedMarkers = input["createLinkedMarkers"]?.Value<bool>() ?? true;
        var usePostCommandIsolate = input["usePostCommandIsolate"]?.Value<bool>() ?? false;
        var offsetMm = input["offset"]?.Value<double>() ?? 1200;

        if (usePostCommandIsolate)
        {
            createLinkedMarkers = false;
            createSectionBox = false;
        }

        if (hostIds.Count == 0 && linkedTargets.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide at least one hostElementIds value or linkedElements target.");

        try
        {
            var uiDoc = new UIDocument(doc);
            // Use ElementIds for selection (host elements + link instance IDs).
            // We intentionally avoid `new Reference(linkedElement).CreateLinkReference(linkInstance)`
            // because building a Reference for some linked sub-elements (e.g. MEP fittings) can
            // throw at the API boundary in ways that bypass our try/catch. Selecting the link
            // instance is consistent with how `highlight_linked_element` works and is robust.
            var selectionElementIds = new HashSet<ElementId>();
            var isolateIds = new HashSet<ElementId>();
            var bbs = new List<BoundingBoxXYZ>();
            var hostResults = new List<object>();
            var linkedResults = new List<object>();
            var pendingMarkers = new List<PendingMarker>();
            int linkedReferenceCount = 0;

            foreach (var rawId in hostIds)
            {
                var id = ToElementId(rawId);
                var element = doc.GetElement(id);
                if (element == null)
                {
                    hostResults.Add(new { elementId = rawId, found = false });
                    continue;
                }

                selectionElementIds.Add(id);
                isolateIds.Add(id);
                AddBoundingBox(bbs, element.get_BoundingBox(null));
                hostResults.Add(new
                {
                    elementId = rawId,
                    found = true,
                    name = element.Name,
                    category = element.Category?.Name ?? ""
                });
            }

            foreach (var token in linkedTargets.OfType<JObject>())
            {
                var instanceId = token["instanceId"]?.Value<long>() ?? 0;
                var linkedElementId = token["linkedElementId"]?.Value<long>() ?? 0;
                if (instanceId <= 0 || linkedElementId <= 0)
                {
                    linkedResults.Add(new
                    {
                        instanceId,
                        linkedElementId,
                        found = false,
                        reason = "instanceId and linkedElementId are required"
                    });
                    continue;
                }

                var linkInstance = doc.GetElement(ToElementId(instanceId)) as RevitLinkInstance;
                var linkDoc = linkInstance?.GetLinkDocument();
                var linkedElement = linkDoc?.GetElement(ToElementId(linkedElementId));
                if (linkInstance == null || linkDoc == null || linkedElement == null)
                {
                    linkedResults.Add(new
                    {
                        instanceId,
                        linkedElementId,
                        found = false,
                        reason = linkInstance == null
                            ? "link instance not found"
                            : linkDoc == null
                                ? "linked document is not loaded"
                                : "linked element not found"
                    });
                    continue;
                }

                // Select the link instance itself; the linked sub-element is highlighted
                // via the DirectShape marker (default path) or by PostCommand isolate.
                selectionElementIds.Add(linkInstance.Id);
                isolateIds.Add(linkInstance.Id);
                linkedReferenceCount++;

                var rawBbox = ResolveLinkedElementBoundingBox(linkedElement, linkDoc);
                var bboxSource = rawBbox.Source;
                var transformedBox = TransformBoundingBox(rawBbox.Box, linkInstance.GetTotalTransform());
                AddBoundingBox(bbs, transformedBox);

                string markerStatus;
                if (!createLinkedMarkers)
                {
                    markerStatus = "skipped:createLinkedMarkers=false";
                }
                else if (transformedBox == null)
                {
                    markerStatus = $"skipped:no-bbox(source={bboxSource})";
                }
                else
                {
                    pendingMarkers.Add(new PendingMarker
                    {
                        Box = transformedBox,
                        InstanceId = instanceId,
                        LinkedElementId = linkedElementId,
                        LinkedElementName = linkedElement.Name,
                        LinkName = linkInstance.Name
                    });
                    markerStatus = $"queued(bboxSource={bboxSource})";
                }

                linkedResults.Add(new
                {
                    instanceId,
                    linkedElementId,
                    found = true,
                    linkedElementName = linkedElement.Name,
                    linkedElementCategory = linkedElement.Category?.Name ?? "",
                    linkName = linkInstance.Name,
                    markerStatus
                });
            }

            if (shouldSelect && selectionElementIds.Count > 0)
                uiDoc.Selection.SetElementIds(selectionElementIds.ToList());

            string? sectionBoxViewName = null;
            var createdMarkers = new List<object>();
            View3D? targetView = null;
            string? postCommandUsed = null;

            if (usePostCommandIsolate && shouldIsolate && selectionElementIds.Count > 0)
            {
                try
                {
                    var cmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.Isolated);
                    uiDoc.Application.PostCommand(cmdId);
                    postCommandUsed = "Isolated";
                }
                catch (Exception ex)
                {
                    postCommandUsed = $"failed: {ex.Message}";
                }

                return CortexResult<object>.Ok(new
                {
                    selectedReferenceCount = selectionElementIds.Count,
                    isolatedElementIds = Array.Empty<long>(),
                    sectionBoxViewName = (string?)null,
                    hostElements = hostResults,
                    linkedElements = linkedResults,
                    linkedMarkers = (object[])Array.Empty<object>(),
                    postCommand = postCommandUsed,
                    message = $"PostCommand isolate dispatched (asynchronous). Selected {selectionElementIds.Count} element(s). Section box and markers were skipped because they are not compatible with the PostCommand path."
                });
            }

            var needsTransaction = pendingMarkers.Count > 0
                || (createSectionBox && bbs.Count > 0)
                || (shouldIsolate && isolateIds.Count > 0);

            if (needsTransaction)
            {
                if (createSectionBox || pendingMarkers.Count > 0)
                    targetView = GetOrActivate3DView(doc, uiDoc);

                using (var tx = new Transaction(doc, "RevitCortex: Show Cross Model Elements"))
                {
                    tx.Start();

                    foreach (var marker in pendingMarkers)
                    {
                        var (dsId, dsStatus) = TryCreateMarkerDirectShape(doc, marker);
                        if (dsId != ElementId.InvalidElementId)
                        {
                            isolateIds.Add(dsId);
                            if (targetView != null)
                                ApplyMarkerOverride(doc, targetView, dsId);

                            createdMarkers.Add(new
                            {
                                directShapeId = ToolHelpers.GetElementIdValue(dsId),
                                instanceId = marker.InstanceId,
                                linkedElementId = marker.LinkedElementId,
                                linkedElementName = marker.LinkedElementName,
                                linkName = marker.LinkName,
                                status = dsStatus
                            });
                        }
                        else
                        {
                            createdMarkers.Add(new
                            {
                                directShapeId = (long?)null,
                                instanceId = marker.InstanceId,
                                linkedElementId = marker.LinkedElementId,
                                linkedElementName = marker.LinkedElementName,
                                linkName = marker.LinkName,
                                status = dsStatus
                            });
                        }
                    }

                    if (createSectionBox && bbs.Count > 0 && targetView != null)
                    {
                        var union = UnionBoundingBoxes(bbs, offsetMm / MmPerFoot);
                        targetView.IsSectionBoxActive = true;
                        targetView.SetSectionBox(union);
                        sectionBoxViewName = targetView.Name;
                    }

                    if (shouldIsolate && isolateIds.Count > 0)
                    {
                        var isolateView = (View?)targetView ?? doc.ActiveView;
                        if (isolateView != null)
                            isolateView.IsolateElementsTemporary(isolateIds.ToList());
                    }

                    tx.Commit();
                }

                if (targetView != null)
                    uiDoc.ActiveView = targetView;
            }

            if (isolateIds.Count > 0)
                uiDoc.ShowElements(isolateIds.ToList());

            var successfulMarkers = createdMarkers.Count(m =>
            {
                var prop = m.GetType().GetProperty("directShapeId");
                return prop?.GetValue(m) is long;
            });
            return CortexResult<object>.Ok(new
            {
                selectedReferenceCount = shouldSelect ? selectionElementIds.Count : 0,
                isolatedElementIds = isolateIds.Select(ToolHelpers.GetElementIdValue).ToArray(),
                sectionBoxViewName,
                hostElements = hostResults,
                linkedElements = linkedResults,
                linkedMarkers = createdMarkers,
                pendingMarkerCount = pendingMarkers.Count,
                postCommand = (string?)null,
                message = $"Prepared {hostResults.Count} host target(s), {linkedResults.Count} linked target(s), {pendingMarkers.Count} pending marker(s), {successfulMarkers} created."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to show cross-model elements: {ex.Message}");
        }
    }

    private sealed class PendingMarker
    {
        public BoundingBoxXYZ Box { get; set; } = null!;
        public long InstanceId { get; set; }
        public long LinkedElementId { get; set; }
        public string LinkedElementName { get; set; } = "";
        public string LinkName { get; set; } = "";
    }

    private sealed class BoundingBoxResolution
    {
        public BoundingBoxXYZ? Box { get; set; }
        public string Source { get; set; } = "none";
    }

    private static BoundingBoxResolution ResolveLinkedElementBoundingBox(Element linkedElement, Document linkDoc)
    {
        var nullViewBox = linkedElement.get_BoundingBox(null);
        if (nullViewBox != null)
            return new BoundingBoxResolution { Box = nullViewBox, Source = "model-null-view" };

        var anyView3D = new FilteredElementCollector(linkDoc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate);
        if (anyView3D != null)
        {
            var viewBox = linkedElement.get_BoundingBox(anyView3D);
            if (viewBox != null)
                return new BoundingBoxResolution { Box = viewBox, Source = "linkdoc-3d-view" };
        }

        try
        {
            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            var geom = linkedElement.get_Geometry(opts);
            if (geom != null)
            {
                var box = ComputeBoxFromGeometry(geom);
                if (box != null)
                    return new BoundingBoxResolution { Box = box, Source = "geometry-extents" };
            }
        }
        catch
        {
        }

        return new BoundingBoxResolution { Box = null, Source = "none" };
    }

    private static BoundingBoxXYZ? ComputeBoxFromGeometry(GeometryElement geom)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool any = false;

        void Consume(GeometryObject obj)
        {
            switch (obj)
            {
                case Solid s when s.Volume > 0:
                    foreach (Edge e in s.Edges)
                    {
                        foreach (var pt in e.Tessellate())
                        {
                            if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X;
                            if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y;
                            if (pt.Z < minZ) minZ = pt.Z; if (pt.Z > maxZ) maxZ = pt.Z;
                            any = true;
                        }
                    }
                    break;
                case GeometryInstance gi:
                    var inst = gi.GetInstanceGeometry();
                    if (inst != null)
                        foreach (GeometryObject sub in inst) Consume(sub);
                    break;
            }
        }

        foreach (GeometryObject obj in geom) Consume(obj);
        if (!any) return null;

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ)
        };
    }

    private static (ElementId Id, string Status) TryCreateMarkerDirectShape(Document doc, PendingMarker marker)
    {
        try
        {
            var offset = MarkerOffsetMm / MmPerFoot;
            var min = new XYZ(marker.Box.Min.X - offset, marker.Box.Min.Y - offset, marker.Box.Min.Z - offset);
            var max = new XYZ(marker.Box.Max.X + offset, marker.Box.Max.Y + offset, marker.Box.Max.Z + offset);

            var (solid, solidStatus) = CreateBoxSolid(min, max);
            if (solid == null) return (ElementId.InvalidElementId, $"solid-failed:{solidStatus}");

            var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.SetShape(new GeometryObject[] { solid });
            ds.Name = $"CrossModelMarker_{marker.LinkedElementId}";

            var commentsParam = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (commentsParam != null && !commentsParam.IsReadOnly)
            {
                var tag = $"{MarkerCommentTag} | instance={marker.InstanceId} | linkedElementId={marker.LinkedElementId} | link={marker.LinkName} | name={marker.LinkedElementName}";
                commentsParam.Set(tag);
            }

            return (ds.Id, "ok");
        }
        catch (Exception ex)
        {
            return (ElementId.InvalidElementId, $"exception:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private static (Solid? Solid, string Status) CreateBoxSolid(XYZ min, XYZ max)
    {
        try
        {
            const double minDim = 1.0 / MmPerFoot;
            var safeMax = new XYZ(
                max.X - min.X < minDim ? min.X + minDim : max.X,
                max.Y - min.Y < minDim ? min.Y + minDim : max.Y,
                max.Z - min.Z < minDim ? min.Z + minDim : max.Z);

            var p0 = new XYZ(min.X, min.Y, min.Z);
            var p1 = new XYZ(safeMax.X, min.Y, min.Z);
            var p2 = new XYZ(safeMax.X, safeMax.Y, min.Z);
            var p3 = new XYZ(min.X, safeMax.Y, min.Z);

            var profile = new List<Curve>
            {
                Line.CreateBound(p0, p1),
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p0)
            };

            var loop = CurveLoop.Create(profile);
            var height = safeMax.Z - min.Z;
            if (height <= 0) return (null, "non-positive-height");

            var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new[] { loop }, XYZ.BasisZ, height);
            return (solid, "ok");
        }
        catch (Exception ex)
        {
            return (null, $"exception:{ex.GetType().Name}");
        }
    }

    private static void ApplyMarkerOverride(Document doc, View view, ElementId dsId)
    {
        try
        {
            var solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            var ogs = new OverrideGraphicSettings();
            var red = new Color(220, 30, 30);
            ogs.SetSurfaceForegroundPatternColor(red);
            ogs.SetProjectionLineColor(red);
            ogs.SetSurfaceTransparency(50);
            if (solidFill != null)
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);

            view.SetElementOverrides(dsId, ogs);
        }
        catch
        {
        }
    }

    private static List<long> ReadLongArray(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return new List<long>();
        return token.ToObject<long[]>()?.ToList() ?? new List<long>();
    }

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId((int)id);
#endif
    }

    private static void AddBoundingBox(ICollection<BoundingBoxXYZ> boxes, BoundingBoxXYZ? box)
    {
        if (box != null) boxes.Add(box);
    }

    private static BoundingBoxXYZ? TransformBoundingBox(BoundingBoxXYZ? box, Transform transform)
    {
        if (box == null) return null;

        var points = new[]
        {
            new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
            new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
            new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
            new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
            new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
            new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
            new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
            new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
        }.Select(transform.OfPoint).ToArray();

        return new BoundingBoxXYZ
        {
            Min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
            Max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z))
        };
    }

    private static BoundingBoxXYZ UnionBoundingBoxes(IEnumerable<BoundingBoxXYZ> boxes, double offset)
    {
        var list = boxes.ToList();
        return new BoundingBoxXYZ
        {
            Min = new XYZ(
                list.Min(b => b.Min.X) - offset,
                list.Min(b => b.Min.Y) - offset,
                list.Min(b => b.Min.Z) - offset),
            Max = new XYZ(
                list.Max(b => b.Max.X) + offset,
                list.Max(b => b.Max.Y) + offset,
                list.Max(b => b.Max.Z) + offset)
        };
    }

    private static View3D? GetOrActivate3DView(Document doc, UIDocument uiDoc)
    {
        if (doc.ActiveView is View3D current && !current.IsTemplate && !current.IsLocked)
            return current;

        var target = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && !v.IsLocked &&
                (v.Name.Contains("{3D}") || v.Name.Contains("Default 3D"))) ??
            new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsLocked);

        if (target != null)
            uiDoc.ActiveView = target;

        return target;
    }
}
