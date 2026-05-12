using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Exports Revit elements to detached DTOs (no Revit object references)
/// that can be safely passed to a background thread for HTTP publishing.
///
/// MUST be called on the Revit main thread. Returns plain dictionaries
/// matching the Elements table schema in PowerBiDatasetSchema.
/// </summary>
public class PowerBiElementExporter
{
    private readonly string _schemaVersion = PowerBiDatasetSchema.CurrentVersion;

    /// <summary>
    /// Snapshots elements from the document into a list of row dictionaries.
    /// Each dictionary maps column name → value, matching the Elements table schema.
    /// </summary>
    public List<Dictionary<string, object?>> ExportElements(
        Document doc,
        string exportRunId,
        DateTime exportedAtUtc,
        IEnumerable<BuiltInCategory>? categoryFilter = null,
        int maxElements = 10_000,
        CancellationToken ct = default)
    {
        string projectId = SafeString(() => doc.ProjectInformation?.UniqueId) ?? "";
        string projectName = SafeString(() => doc.ProjectInformation?.Name) ?? doc.Title ?? "";
        string documentGuid = GetDocumentGuid(doc);

        var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

        // Convert to HashSet for O(1) per-element lookup instead of O(n) linear scan.
        HashSet<BuiltInCategory>? categorySet = categoryFilter != null
            ? new HashSet<BuiltInCategory>(categoryFilter)
            : null;

        var rows = new List<Dictionary<string, object?>>();
        int count = 0;

        foreach (Element elem in collector)
        {
            ct.ThrowIfCancellationRequested();
            if (count >= maxElements) break;
            if (elem == null) continue;

            // Category filter
            if (categorySet != null)
            {
                BuiltInCategory elemBic = BuiltInCategory.INVALID;
                try
                {
#if REVIT2024_OR_GREATER
                    if (elem.Category?.Id != null)
                        elemBic = (BuiltInCategory)(int)elem.Category.Id.Value;
#else
                    if (elem.Category?.Id != null)
                        elemBic = (BuiltInCategory)elem.Category.Id.IntegerValue;
#endif
                }
                catch { continue; }

                if (!categorySet.Contains(elemBic)) continue;
            }

            var row = BuildRow(doc, elem, exportRunId, exportedAtUtc,
                projectId, projectName, documentGuid);
            if (row != null)
            {
                rows.Add(row);
                count++;
            }
        }

        return rows;
    }

    private Dictionary<string, object?>? BuildRow(
        Document doc,
        Element elem,
        string exportRunId,
        DateTime exportedAtUtc,
        string projectId,
        string projectName,
        string documentGuid)
    {
        try
        {
            long elementId = GetElementIdValue(elem.Id);
            string uniqueId = SafeString(() => elem.UniqueId) ?? "";
            string category = SafeString(() => elem.Category?.Name) ?? "";
            string ostCode = GetOstCode(elem);
            string categoryType = SafeString(() => elem.Category?.CategoryType.ToString()) ?? "";
            string familyName = "";
            string typeName = "";

            // Family + type name
            try
            {
                var typeId = elem.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var elemType = doc.GetElement(typeId);
                    if (elemType != null)
                    {
                        typeName = SafeString(() => elemType.Name) ?? "";
                        if (elemType is FamilySymbol fs)
                            familyName = SafeString(() => fs.FamilyName) ?? "";
                    }
                }
            }
            catch { }

            string level = GetParamString(elem, BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                        ?? GetParamString(elem, BuiltInParameter.FAMILY_LEVEL_PARAM)
                        ?? "";
            string workset = SafeString(() =>
            {
                if (!doc.IsWorkshared) return "";
                var ws = doc.GetWorksetTable().GetWorkset(elem.WorksetId);
                return ws?.Name ?? "";
            }) ?? "";

            string phaseCreated = "";
            string phaseDemolished = "";
            try
            {
                var phaseCreatedId = elem.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId();
                if (phaseCreatedId != null && phaseCreatedId != ElementId.InvalidElementId)
                    phaseCreated = doc.GetElement(phaseCreatedId)?.Name ?? "";
                var phaseDemoId = elem.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED)?.AsElementId();
                if (phaseDemoId != null && phaseDemoId != ElementId.InvalidElementId)
                    phaseDemolished = doc.GetElement(phaseDemoId)?.Name ?? "";
            }
            catch { }

            string name = SafeString(() => elem.Name) ?? "";
            string mark = GetParamString(elem, BuiltInParameter.ALL_MODEL_MARK) ?? "";
            string comments = GetParamString(elem, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS) ?? "";

            double volume = GetParamDouble(elem, BuiltInParameter.HOST_VOLUME_COMPUTED)
                         ?? GetParamDouble(elem, BuiltInParameter.STRUCTURAL_SECTION_AREA) ?? 0;
            double area = GetParamDouble(elem, BuiltInParameter.HOST_AREA_COMPUTED)
                       ?? GetParamDouble(elem, BuiltInParameter.ROOM_AREA) ?? 0;
            double length = GetParamDouble(elem, BuiltInParameter.CURVE_ELEM_LENGTH)
                         ?? GetParamDouble(elem, BuiltInParameter.INSTANCE_LENGTH_PARAM) ?? 0;

            // Bounding box
            double bbMinX = 0, bbMinY = 0, bbMinZ = 0;
            double bbMaxX = 0, bbMaxY = 0, bbMaxZ = 0;
            try
            {
                var bb = elem.get_BoundingBox(null);
                if (bb != null)
                {
                    bbMinX = bb.Min.X; bbMinY = bb.Min.Y; bbMinZ = bb.Min.Z;
                    bbMaxX = bb.Max.X; bbMaxY = bb.Max.Y; bbMaxZ = bb.Max.Z;
                }
            }
            catch { }

            var row = new Dictionary<string, object?>
            {
                ["_SchemaVersion"]  = _schemaVersion,
                ["ExportRunId"]     = exportRunId,
                ["ExportedAtUtc"]   = exportedAtUtc.ToString("o"),
                ["ProjectId"]       = projectId,
                ["ProjectName"]     = projectName,
                ["DocumentGuid"]    = documentGuid,
                ["ElementId"]       = elementId,
                ["UniqueId"]        = uniqueId,
                ["Category"]        = category,
                ["OstCode"]         = ostCode,
                ["CategoryType"]    = categoryType,
                ["FamilyName"]      = familyName,
                ["TypeName"]        = typeName,
                ["Level"]           = level,
                ["Workset"]         = workset,
                ["PhaseCreated"]    = phaseCreated,
                ["PhaseDemolished"] = phaseDemolished,
                ["Name"]            = name,
                ["Mark"]            = mark,
                ["Comments"]        = comments,
                ["Volume"]          = volume,
                ["Area"]            = area,
                ["Length"]          = length,
                ["BoundingBoxMinX"] = bbMinX,
                ["BoundingBoxMinY"] = bbMinY,
                ["BoundingBoxMinZ"] = bbMinZ,
                ["BoundingBoxMaxX"] = bbMaxX,
                ["BoundingBoxMaxY"] = bbMaxY,
                ["BoundingBoxMaxZ"] = bbMaxZ
            };
            return row;
        }
        catch
        {
            return null;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static long GetElementIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    private static string GetOstCode(Element elem)
    {
        try
        {
            if (elem.Category?.Id == null) return "";
            long idVal = GetElementIdValue(elem.Category.Id);
            // Safe cast: BuiltInCategory is int-backed. Truncate to int only after
            // range-checking, mirroring ParameterDiscoveryService.TryGetBuiltInCategory.
            if (idVal < int.MinValue || idVal > int.MaxValue) return $"OST_{idVal}";
            var bic = (BuiltInCategory)(int)idVal;
            if (Enum.IsDefined(typeof(BuiltInCategory), bic))
                return bic.ToString();
            return $"OST_{idVal}";
        }
        catch { return ""; }
    }

    private static string? GetParamString(Element elem, BuiltInParameter bip)
    {
        try
        {
            var p = elem.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;
            return p.AsString() ?? p.AsValueString();
        }
        catch { return null; }
    }

    private static double? GetParamDouble(Element elem, BuiltInParameter bip)
    {
        try
        {
            var p = elem.get_Parameter(bip);
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
            return p.AsDouble();
        }
        catch { return null; }
    }

    private static string? SafeString(Func<string?> fn)
    {
        try { return fn(); }
        catch { return null; }
    }

    private static string GetDocumentGuid(Document doc)
    {
        try
        {
            var cloudPath = doc.GetCloudModelPath();
            if (cloudPath != null)
                return cloudPath.ToString();
        }
        catch { }

        try { return doc.PathName ?? ""; }
        catch { return ""; }
    }

    /// <summary>
    /// Builds the Metadata table rows for a publish run.
    /// </summary>
    public static List<Dictionary<string, object?>> BuildMetadataRows(
        Document doc,
        string exportRunId,
        DateTime exportedAtUtc,
        string revitCortexVersion = "1.0")
    {
        string projectId = "";
        string projectName = "";
        string documentGuid = "";
        try { projectId = doc.ProjectInformation?.UniqueId ?? ""; } catch { }
        try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
        try { documentGuid = GetDocumentGuid(doc); } catch { }

        var now = exportedAtUtc.ToString("o");
        var rows = new List<Dictionary<string, object?>>();

        void AddRow(string key, string value) => rows.Add(new Dictionary<string, object?>
        {
            ["_SchemaVersion"] = PowerBiDatasetSchema.CurrentVersion,
            ["Key"]            = key,
            ["Value"]          = value,
            ["UpdatedAtUtc"]   = now
        });

        AddRow("SchemaVersion", PowerBiDatasetSchema.CurrentVersion);
        AddRow("ExportRunId", exportRunId);
        AddRow("CreatedBy", "RevitCortex");
        AddRow("CreatedAtUtc", now);
        AddRow("ProjectName", projectName);
        AddRow("ProjectId", projectId);
        AddRow("DocumentGuid", documentGuid);
        AddRow("RevitCortexVersion", revitCortexVersion);

        return rows;
    }
}
