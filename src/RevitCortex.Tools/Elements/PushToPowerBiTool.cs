using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Exports Revit element data (parameters) to a CSV file on a local/OneDrive folder
/// so that Power BI can pick it up via scheduled refresh from SharePoint/OneDrive.
/// </summary>
public class PushToPowerBiTool : ICortexTool
{
    public string Name => "push_to_powerbi";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports element data to a CSV file in a local/OneDrive folder for Power BI refresh.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var scheduleIds = input["scheduleIds"]?.ToObject<List<long>>() ?? new List<long>();
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var parameterNames = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? false;
        var maxElements = input["maxElements"]?.Value<int>() ?? 10000;
        var outputFolder = input["outputFolder"]?.Value<string>();
        var fileName = input["fileName"]?.Value<string>();
        // Optional: explicit column mapping with aliases and formulas. When set,
        // overrides the parameter discovery + ordering logic.
        var columnMappingsRaw = input["columnMappings"] as JArray;
        // Optional: per-column type hints + .pq generation. When provided we
        // emit a sibling Power Query file with explicit Table.TransformColumnTypes
        // and, for numeric-typed columns, write <col>_Raw companions in invariant
        // culture so PBI doesn't have to strip "1500 mm" unit suffixes itself.
        var columnTypesRaw = input["columnTypes"] as JArray;
        var schemaMappingMode = input["schemaMappingMode"]?.Value<string>() ?? "Auto";

        // ── Schedule mode: one CSV per schedule, columns = schedule fields ──
        if (scheduleIds.Count > 0)
        {
            return ExportSchedules(doc, scheduleIds, outputFolder);
        }

        // Default: OneDrive GPA Ingegneria Srl folder, subfolder RevitCortex/<DocName>
        if (string.IsNullOrEmpty(outputFolder))
        {
            var oneDrive = FindOneDriveFolder();
            var docName = SanitizeFolderName(doc.Title);
            outputFolder = Path.Combine(oneDrive, "RevitCortex", docName);
        }

        if (string.IsNullOrEmpty(fileName))
            fileName = $"elements_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

        if (!fileName!.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            fileName += ".csv";

        try
        {
            Directory.CreateDirectory(outputFolder);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Cannot create output folder '{outputFolder}': {ex.Message}",
                suggestion: "Check that the path exists and you have write permission.");
        }

        var filePath = Path.Combine(outputFolder, fileName);

        // Also write a metadata sidecar so PBI knows when the data was refreshed
        var metaPath = Path.Combine(outputFolder, "last_refresh.json");

        // Document identity columns (shared between CSV body and manifest):
        //   DocumentPath: file location (cloud URN or local path), not stable
        //                 across rename/move but useful for human filtering.
        //   EpisodeId:    immutable Revit document GUID; recommended cross-
        //                 file join key. Stable across rename/move.
        var documentPath = GetDocumentPath(doc);
        var episodeId    = GetEpisodeId(doc);

        // Scope filter (SheetLink-style: WholeModel | ActiveView | Selection)
        var scopeMode = (input["scopeMode"]?.Value<string>() ?? "WholeModel").Trim();
        var selectionIds = input["selectionIds"]?.ToObject<List<long>>();
        var activeViewIdToken = input["activeViewId"];

        try
        {
            // Build the base collector that respects the chosen scope
            FilteredElementCollector BaseCollector()
            {
                if (scopeMode.Equals("Selection", StringComparison.OrdinalIgnoreCase) &&
                    selectionIds != null && selectionIds.Count > 0)
                {
#if REVIT2024_OR_GREATER
                    var ids = selectionIds.Select(id => new ElementId(id)).ToList();
#else
                    var ids = selectionIds.Select(id => new ElementId((int)id)).ToList();
#endif
                    return new FilteredElementCollector(doc, ids);
                }
                if (scopeMode.Equals("ActiveView", StringComparison.OrdinalIgnoreCase))
                {
                    ElementId viewId;
                    if (activeViewIdToken != null && activeViewIdToken.Type != JTokenType.Null)
                    {
                        var raw = activeViewIdToken.Value<long>();
#if REVIT2024_OR_GREATER
                        viewId = new ElementId(raw);
#else
                        viewId = new ElementId((int)raw);
#endif
                    }
                    else
                    {
                        viewId = doc.ActiveView?.Id ?? ElementId.InvalidElementId;
                    }
                    if (viewId != ElementId.InvalidElementId)
                        return new FilteredElementCollector(doc, viewId);
                }
                return new FilteredElementCollector(doc);
            }

            // Collect elements
            IEnumerable<Element> elements;
            if (categories.Count > 0)
            {
                var catIds = categories
                    .Select(c => CategoryResolver.ResolveToId(doc, c))
                    .Where(id => id != ElementId.InvalidElementId)
                    .ToList();

                if (catIds.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"None of the requested categories could be resolved: {string.Join(", ", categories)}",
                        suggestion: "Use OST_* codes (e.g. OST_Walls) or exact display names in the model locale.");

                elements = catIds.SelectMany(catId =>
                    BaseCollector().OfCategoryId(catId).WhereElementIsNotElementType());
            }
            else
            {
                elements = BaseCollector()
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null);
            }

            var elemList = elements.Take(maxElements).ToList();
            if (elemList.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No elements found matching the requested categories.");

            // Discover parameter columns (sample first 100 for performance).
            // We skip parameter names that collide with the built-in column
            // headers we always emit (ElementId, UniqueId, Category, Family,
            // Type, DocumentTitle, DocumentGuid). Otherwise Power Query would
            // see two columns named "Family" (one from FamilyName, one from
            // the user-selected param) and complain about ambiguity. The
            // built-in column already carries the relevant value.
            var builtInColumnNames = new HashSet<string>(
                new[] { "ElementId", "UniqueId", "Category", "Family", "Type", "DocumentTitle", "DocumentGuid" },
                StringComparer.OrdinalIgnoreCase);
            var instanceParamNames = new LinkedHashSet();
            var typeParamNames = new LinkedHashSet();
            var typeCache = new Dictionary<ElementId, Element?>();

            foreach (var elem in elemList.Take(100))
            {
                foreach (Parameter p in elem.Parameters)
                {
                    var name = p.Definition.Name;
                    if (builtInColumnNames.Contains(name)) continue;
                    if (parameterNames.Count == 0 || parameterNames.Contains(name))
                        instanceParamNames.Add(name);
                }

                if (includeTypeParams)
                {
                    var typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        if (!typeCache.TryGetValue(typeId, out var typeElem))
                        {
                            typeElem = doc.GetElement(typeId);
                            typeCache[typeId] = typeElem;
                        }
                        if (typeElem != null)
                        {
                            foreach (Parameter p in typeElem.Parameters)
                            {
                                var name = p.Definition.Name;
                                // Type params are emitted as "[Type] <name>" so they
                                // can't collide with the built-in headers, but we still
                                // filter explicit user requests for built-in names.
                                if (builtInColumnNames.Contains(name)) continue;
                                if (parameterNames.Count == 0 || parameterNames.Contains(name))
                                    typeParamNames.Add(name);
                            }
                        }
                    }
                }
            }

            // Prevent duplicate columns (instance takes priority)
            foreach (var name in instanceParamNames.Items)
                typeParamNames.Remove(name);

            // Write CSV
            var sb = new StringBuilder();
            int columnCount;

            if (columnMappingsRaw != null && columnMappingsRaw.Count > 0)
            {
                // ── Explicit mapping mode: user-defined columns ──
                var mappings = ParseMappings(columnMappingsRaw);

                // ElementId is forced as first column even in explicit mapping mode,
                // because losing the join key silently breaks PBI relationships.
                // The CSV header always starts with "ElementId".
                bool userHasElementId = mappings.Any(m =>
                    string.Equals(EffectiveHeader(m), "ElementId", StringComparison.OrdinalIgnoreCase));

                var headers = new List<string>();
                if (!userHasElementId) headers.Add("ElementId");
                headers.AddRange(mappings.Select(m => EffectiveHeader(m)));
                columnCount = headers.Count;

                sb.AppendLine(ToCsvRow(headers.Select(CsvEscape)));

                foreach (var elem in elemList)
                {
                    var typeId = elem.GetTypeId();
                    if (!typeCache.TryGetValue(typeId, out var typeElem))
                    {
                        typeElem = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
                        typeCache[typeId] = typeElem;
                    }

                    var row = new List<string>(headers.Count);
                    if (!userHasElementId)
                        row.Add(ToolHelpers.GetElementIdValue(elem.Id).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    foreach (var m in mappings)
                    {
                        var raw = ResolveMappingValue(elem, typeElem as ElementType, m);
                        row.Add(CsvEscape(raw));
                    }
                    sb.AppendLine(string.Join(",", row));
                }
            }
            else
            {
                // ── Discovery mode (default, retro-compatible) ──
                // When schemaMappingMode != "Auto", numeric-typed columns get a
                // sibling "<col>_Raw" companion containing the invariant-culture
                // raw value. The display column keeps the locale-formatted value
                // with unit suffix (e.g. "1500 mm") for human readability.
                var columnTypes = ParseColumnTypes(columnTypesRaw);
                bool useTypeMapping = !string.Equals(schemaMappingMode, "Auto", StringComparison.OrdinalIgnoreCase)
                                    && columnTypes.Count > 0;

                // Built-in columns, in order:
                //   ElementId / UniqueId  — element identity (UniqueId stable across
                //                           purge / family reload)
                //   Category / Family / Type — built-in classifiers
                //   DocumentTitle  — human label
                //   DocumentPath   — file system path (cloud URN or local) — useful
                //                    for filtering, NOT stable across rename
                //   EpisodeId      — immutable Revit document GUID (extracted from
                //                    UniqueId prefix); recommended cross-file join key
                var header = new List<string> { "ElementId", "UniqueId", "Category", "Family", "Type", "DocumentTitle", "DocumentPath", "EpisodeId" };
                var rawCompanionInst = new HashSet<string>(); // instance param names that need _Raw
                var rawCompanionType = new HashSet<string>(); // type param names (without [Type] prefix) that need _Raw

                foreach (var name in instanceParamNames.Items)
                {
                    header.Add(name);
                    if (useTypeMapping && IsNumericMapped(columnTypes, name))
                    {
                        header.Add(name + "_Raw");
                        rawCompanionInst.Add(name);
                    }
                }
                if (includeTypeParams)
                {
                    foreach (var name in typeParamNames.Items)
                    {
                        var prefixed = $"[Type] {name}";
                        header.Add(prefixed);
                        if (useTypeMapping && IsNumericMapped(columnTypes, prefixed))
                        {
                            header.Add(prefixed + "_Raw");
                            rawCompanionType.Add(name);
                        }
                    }
                }
                columnCount = header.Count;
                sb.AppendLine(ToCsvRow(header));

                foreach (var elem in elemList)
                {
                    var typeId = elem.GetTypeId();
                    if (!typeCache.TryGetValue(typeId, out var typeElem))
                    {
                        typeElem = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
                        typeCache[typeId] = typeElem;
                    }

                    var row = new List<string>
                    {
                        ToolHelpers.GetElementIdValue(elem.Id).ToString(CultureInfo.InvariantCulture),
                        CsvEscape(elem.UniqueId ?? ""),
                        CsvEscape(elem.Category?.Name ?? ""),
                        CsvEscape(elem.LookupParameter("Family Name")?.AsString()
                            ?? (typeElem as ElementType)?.FamilyName ?? ""),
                        CsvEscape(elem.LookupParameter("Type Name")?.AsValueString()
                            ?? (typeElem as ElementType)?.Name ?? ""),
                        CsvEscape(doc.Title ?? ""),
                        CsvEscape(documentPath),
                        CsvEscape(episodeId)
                    };

                    foreach (var name in instanceParamNames.Items)
                    {
                        var p = elem.LookupParameter(name);
                        row.Add(CsvEscape(p != null ? GetParamDisplayValue(p) : ""));
                        if (rawCompanionInst.Contains(name))
                            row.Add(p != null ? GetParamRawValue(p) : "");
                    }

                    if (includeTypeParams)
                    {
                        foreach (var name in typeParamNames.Items)
                        {
                            var p = typeElem?.LookupParameter(name);
                            row.Add(CsvEscape(p != null ? GetParamDisplayValue(p) : ""));
                            if (rawCompanionType.Contains(name))
                                row.Add(p != null ? GetParamRawValue(p) : "");
                        }
                    }

                    sb.AppendLine(ToCsvRow(row));
                }

                // Power Query sidecar (only when mapping is requested and there's
                // at least one non-auto column type to transform).
                if (useTypeMapping)
                {
                    try { WritePowerQuerySidecar(filePath, header, columnTypes); }
                    catch { /* never block export on .pq emission failure */ }
                }
            }

            WriteCsvAtomic(filePath, sb.ToString());

            // Write metadata sidecar with manifest section that declares schema,
            // grain, primary key and suggested join keys. Power BI doesn't read
            // this directly but it documents the data model for downstream tools
            // and for users wiring relationships manually in Power BI Desktop.
            // For multi-source dashboards: join on (DocumentGuid, UniqueId).
            var meta = new JObject
            {
                ["refreshed_at"]   = DateTime.UtcNow.ToString("o"),
                ["document"]       = doc.Title,
                ["document_path"]  = documentPath,
                ["episode_id"]     = episodeId,
                ["element_count"]  = elemList.Count,
                ["categories"]     = categories.Count > 0 ? string.Join(", ", categories) : "all",
                ["file"]           = fileName,
                ["column_count"]   = columnCount,
                ["mode"]           = columnMappingsRaw != null && columnMappingsRaw.Count > 0 ? "mapping" : "discovery",
                ["schema_version"] = "2.0",  // bumped from 1.0: DocumentGuid → DocumentPath; new EpisodeId column
                ["manifest"] = new JObject
                {
                    ["schemaVersion"] = "2.0",
                    ["grain"]         = "one row per element",
                    ["primaryKey"]    = new JArray("EpisodeId", "UniqueId"),
                    ["stableJoinKeys"] = new JObject
                    {
                        ["EpisodeId"]    = "immutable Revit document GUID extracted from UniqueId prefix; same value for all elements of the same .rvt, survives rename/move/cloud-roundtrip — RECOMMENDED join key for cross-file dashboards",
                        ["UniqueId"]     = "stable per element across edits, purge and family reload — recommended element-level key",
                        ["DocumentPath"] = "file system path (cloud URN or local) of the source doc — useful for filtering / drill-back to Revit, NOT stable across file moves",
                        ["DocumentTitle"]= "human-readable doc name — for labels only, NOT for joins (can collide between models with same name)",
                        ["ElementId"]    = "fast join inside single doc + export run; NOT globally unique"
                    },
                    ["suggestedRelationships"] = new JArray(
                        "Cross-file element drill-through: join on (EpisodeId, UniqueId) — works correctly even if the .rvt is moved or saved-as.",
                        "Multi-model dashboards: filter by EpisodeId to discriminate documents; DocumentTitle as the slicer label.",
                        "Element ⋈ schedule join: still (EpisodeId, UniqueId) — both files share the same EpisodeId of the parent doc."
                    )
                }
            };
            File.WriteAllText(metaPath, meta.ToString(), Encoding.UTF8);

            return CortexResult<object>.Ok(new
            {
                filePath,
                metaPath,
                elementCount = elemList.Count,
                columnCount,
                instanceParameterCount = instanceParamNames.Count,
                typeParameterCount = typeParamNames.Count,
                mappingMode = columnMappingsRaw != null && columnMappingsRaw.Count > 0,
                tip = "Connect Power BI Desktop to this folder via 'Get Data → Folder' or 'SharePoint Folder' if synced to OneDrive."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Export failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ExportSchedules(Document doc, List<long> scheduleIds, string? outputFolder)
    {
        // Document identity columns — see element-mode branch for the rationale.
        var documentPath = GetDocumentPath(doc);
        var episodeId    = GetEpisodeId(doc);

        if (string.IsNullOrEmpty(outputFolder))
        {
            var oneDrive = FindOneDriveFolder();
            var docName = SanitizeFolderName(doc.Title);
            outputFolder = Path.Combine(oneDrive, "RevitCortex", docName);
        }

        try { Directory.CreateDirectory(outputFolder); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Cannot create output folder '{outputFolder}': {ex.Message}");
        }

        var written = new List<object>();
        int totalRows = 0;

        foreach (var schId in scheduleIds)
        {
#if REVIT2024_OR_GREATER
            var elementId = new ElementId(schId);
#else
            var elementId = new ElementId((int)schId);
#endif
            if (doc.GetElement(elementId) is not ViewSchedule view)
                continue;

            var safeName = SanitizeFolderName(view.Name);
            var csvName = $"schedule_{safeName}.csv";
            var csvPath = Path.Combine(outputFolder, csvName);

            try
            {
                // Schedule rows are sourced per-element (FilteredElementCollector
                // on the schedule's id), not by reading body cells. This gives us
                // a reliable ElementId per row and skips group/total rows that
                // have no element backing.
                var sb = new StringBuilder();

                // Resolve schedule fields (heading + parameter binding)
                var def = view.Definition;
                int fieldCount = def?.GetFieldCount() ?? 0;
                var fieldHeadings = new List<string>(fieldCount);
                var fieldBips = new List<BuiltInParameter?>(fieldCount);
                for (int i = 0; i < fieldCount; i++)
                {
                    string heading = $"Col{i + 1}";
                    BuiltInParameter? bip = null;
                    try
                    {
                        var f = def!.GetField(i);
                        if (f != null && !f.IsHidden)
                        {
                            heading = !string.IsNullOrEmpty(f.ColumnHeading) ? f.ColumnHeading :
                                      (f.GetName() ?? heading);
                            if (f.ParameterId != null && f.ParameterId != ElementId.InvalidElementId)
                            {
                                long pid = ToolHelpers.GetElementIdValue(f.ParameterId);
                                if (pid < 0) bip = (BuiltInParameter)pid;
                            }
                        }
                        else
                        {
                            continue; // skip hidden field
                        }
                    }
                    catch { continue; }
                    fieldHeadings.Add(heading);
                    fieldBips.Add(bip);
                }

                // Built-in prefix: ElementId / UniqueId / DocumentTitle /
                // DocumentPath / EpisodeId. See element-mode branch for column
                // semantics. EpisodeId is the recommended cross-file join key.
                var headerCells = new List<string> { "ElementId", "UniqueId", "DocumentTitle", "DocumentPath", "EpisodeId" };
                headerCells.AddRange(fieldHeadings.Select(CsvEscape));
                sb.AppendLine(string.Join(",", headerCells));

                // Enumerate elements visible in the schedule
                var schedElements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements();

                int written_rows = 0;
                foreach (var elem in schedElements)
                {
                    if (elem == null) continue;

                    Element? elemType = null;
                    try
                    {
                        var typeId = elem.GetTypeId();
                        if (typeId != null && typeId != ElementId.InvalidElementId)
                            elemType = doc.GetElement(typeId);
                    }
                    catch { }

                    var row = new List<string>(fieldHeadings.Count + 5)
                    {
                        ToolHelpers.GetElementIdValue(elem.Id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        CsvEscape(elem.UniqueId ?? ""),
                        CsvEscape(doc.Title ?? ""),
                        CsvEscape(documentPath),
                        CsvEscape(episodeId)
                    };
                    for (int i = 0; i < fieldHeadings.Count; i++)
                    {
                        var bip = fieldBips[i];
                        Parameter? p = null;
                        if (bip.HasValue)
                        {
                            try { p = elem.get_Parameter(bip.Value); } catch { }
                            if ((p == null || !p.HasValue) && elemType != null)
                            {
                                try { p = elemType.get_Parameter(bip.Value); } catch { }
                            }
                        }
                        else
                        {
                            try { p = elem.LookupParameter(fieldHeadings[i]); } catch { }
                            if ((p == null || !p.HasValue) && elemType != null)
                            {
                                try { p = elemType.LookupParameter(fieldHeadings[i]); } catch { }
                            }
                        }
                        row.Add(CsvEscape(p != null ? GetParamDisplayValue(p) : ""));
                    }
                    sb.AppendLine(string.Join(",", row));
                    written_rows++;
                }

                WriteCsvAtomic(csvPath, sb.ToString());
                totalRows += written_rows;
                written.Add(new
                {
                    schedule = view.Name,
                    scheduleId = schId,
                    file = csvPath,
                    columns = fieldHeadings.Count + 1,
                    rows = written_rows
                });
            }
            catch (Exception ex)
            {
                written.Add(new
                {
                    schedule = view.Name,
                    scheduleId = schId,
                    error = ex.Message
                });
            }
        }

        // Metadata sidecar with manifest section - see element-export branch for the same shape.
        var meta = new JObject
        {
            ["refreshed_at"]   = DateTime.UtcNow.ToString("o"),
            ["document"]       = doc.Title,
            ["document_path"]  = documentPath,
            ["episode_id"]     = episodeId,
            ["mode"]           = "schedule",
            ["schedule_count"] = scheduleIds.Count,
            ["total_rows"]     = totalRows,
            ["schema_version"] = "2.0",
            ["manifest"] = new JObject
            {
                ["schemaVersion"] = "2.0",
                ["grain"]         = "one row per scheduled element",
                // PK is (EpisodeId, UniqueId). ScheduleName is encoded in the
                // FILENAME (schedule_<Name>.csv), not in the CSV body — so it's
                // not part of the in-file PK. To distinguish schedules in a
                // unioned table, derive ScheduleName from the source filename
                // in Power Query (e.g. via Folder.Files [Name] column).
                ["primaryKey"]    = new JArray("EpisodeId", "UniqueId"),
                ["stableJoinKeys"] = new JObject
                {
                    ["EpisodeId"]    = "immutable Revit document GUID extracted from UniqueId prefix; same value for all elements of the same .rvt, survives rename/move/cloud-roundtrip — RECOMMENDED join key for cross-file dashboards",
                    ["UniqueId"]     = "stable per element across edits — recommended element-level key",
                    ["DocumentPath"] = "file system path (cloud URN or local) of the source doc — useful for filtering / drill-back to Revit",
                    ["DocumentTitle"]= "human-readable doc name — for labels only, NOT for joins",
                    ["ElementId"]    = "fast join inside single doc + export run; NOT globally unique"
                },
                ["suggestedRelationships"] = new JArray(
                    "Join schedule_*.csv to an element-export CSV from the same doc on (EpisodeId, UniqueId) for cell-level drill-through. EpisodeId is the same across all CSVs from the same .rvt.",
                    "Multiple schedule_*.csv files share EpisodeId + UniqueId namespace, so they can be unioned in Power Query. Add a 'ScheduleName' column from the source filename to discriminate.",
                    "Multi-model dashboards: filter by EpisodeId to discriminate documents (more robust than DocumentPath which changes on file move)."
                )
            }
        };
        File.WriteAllText(Path.Combine(outputFolder, "last_refresh.json"), meta.ToString(), Encoding.UTF8);

        return CortexResult<object>.Ok(new
        {
            mode = "schedule",
            outputFolder,
            scheduleCount = scheduleIds.Count,
            rowCount = totalRows,
            files = written,
            tip = "Connect Power BI Desktop to this folder via 'Get Data → Folder' (one schedule per CSV)."
        });
    }

    /// <summary>
    /// Document path identity (cloud URN or local file path). Used for the
    /// <c>DocumentPath</c> CSV column. Returns the cloud model path when
    /// available, otherwise <see cref="Document.PathName"/>. This is NOT
    /// a stable doc identifier — file moves/renames change it. For a stable
    /// identifier use <see cref="GetEpisodeId"/>.
    /// </summary>
    private static string GetDocumentPath(Document doc)
    {
        try
        {
            var cloudPath = doc.GetCloudModelPath();
            if (cloudPath != null) return cloudPath.ToString();
        }
        catch { /* not a cloud doc */ }
        try { return doc.PathName ?? ""; } catch { return ""; }
    }

    /// <summary>
    /// Returns the Revit "EpisodeId" — the immutable internal document
    /// identifier. Same value for every element in the same .rvt; survives
    /// rename, move, cloud-roundtrip. Extracted from the first 36 chars of
    /// any element's UniqueId (Revit's UniqueId format is
    /// <c>8-4-4-4-12-8</c> hex, where the leading 36 chars are the
    /// document's GUID and the trailing 8 chars are the element's id).
    /// We read it from <see cref="Document.ProjectInformation"/> because
    /// that element exists in every doc and avoids enumerating model elements.
    /// Documented by Jeremy Tammik / Building Coder as the canonical doc GUID.
    /// Returns empty string when the API can't fulfill it (e.g. brand-new
    /// unsaved family).
    /// </summary>
    private static string GetEpisodeId(Document doc)
    {
        try
        {
            var pi = doc?.ProjectInformation;
            var uid = pi?.UniqueId;
            if (!string.IsNullOrEmpty(uid) && uid!.Length >= 36)
                return uid.Substring(0, 36);
        }
        catch { /* malformed doc */ }
        return "";
    }

    private static string FindOneDriveFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Look for GPA-branded OneDrive first, then generic OneDrive
        foreach (var candidate in new[]
        {
            Path.Combine(userProfile, "OneDrive - GPA Ingegneria Srl"),
            Path.Combine(userProfile, "OneDrive - GPA Partners"),
            Path.Combine(userProfile, "OneDrive")
        })
        {
            if (Directory.Exists(candidate))
                return candidate;
        }
        // Fallback: Desktop
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    /// <summary>
    /// Mapping POCO mirrored from the plugin's ColumnMapping class. We can't
    /// reference the plugin assembly here, so we re-declare the shape locally.
    /// </summary>
    private class MappingDef
    {
        public string Source = "param";
        public string Header = "";
        public string ParameterName = "";
        public string Scope = "Instance";
        public string FieldName = "";
        public string Formula = "";
    }

    private static List<MappingDef> ParseMappings(JArray arr)
    {
        var result = new List<MappingDef>();
        foreach (var item in arr.OfType<JObject>())
        {
            result.Add(new MappingDef
            {
                Source = item["Source"]?.ToString() ?? item["source"]?.ToString() ?? "param",
                Header = item["Header"]?.ToString() ?? item["header"]?.ToString() ?? "",
                ParameterName = item["ParameterName"]?.ToString() ?? item["parameterName"]?.ToString() ?? "",
                Scope = item["Scope"]?.ToString() ?? item["scope"]?.ToString() ?? "Instance",
                FieldName = item["FieldName"]?.ToString() ?? item["fieldName"]?.ToString() ?? "",
                Formula = item["Formula"]?.ToString() ?? item["formula"]?.ToString() ?? ""
            });
        }
        return result;
    }

    private static string EffectiveHeader(MappingDef m)
    {
        if (!string.IsNullOrWhiteSpace(m.Header)) return m.Header;
        if (m.Source == "field") return string.IsNullOrEmpty(m.FieldName) ? "Field" : m.FieldName;
        if (m.Source == "formula") return "Computed";
        if (m.Source == "param")
            return m.Scope == "Type" ? $"[Type] {m.ParameterName}" : m.ParameterName;
        return "Column";
    }

    /// <summary>
    /// Resolves a mapping into a string value for the CSV cell.
    /// Built-in fields, parameters (instance/type), and formulas with {tokens} are supported.
    /// </summary>
    private static string ResolveMappingValue(Element elem, ElementType? typeElem, MappingDef m)
    {
        try
        {
            switch (m.Source)
            {
                case "field":
                    return ResolveBuiltInField(elem, typeElem, m.FieldName);

                case "param":
                    {
                        Parameter? p = m.Scope == "Type"
                            ? typeElem?.LookupParameter(m.ParameterName)
                            : elem.LookupParameter(m.ParameterName);
                        if (p == null) return "";
                        return GetParamDisplayValue(p);
                    }

                case "formula":
                    return EvaluateFormula(elem, typeElem, m.Formula);

                default:
                    return "";
            }
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveBuiltInField(Element elem, ElementType? typeElem, string field)
    {
        switch (field)
        {
            case "ElementId":
                return ToolHelpers.GetElementIdValue(elem.Id).ToString();
            case "Category":
                return elem.Category?.Name ?? "";
            case "Family":
                return elem.LookupParameter("Family Name")?.AsString() ?? typeElem?.FamilyName ?? "";
            case "Type":
                return elem.LookupParameter("Type Name")?.AsValueString() ?? typeElem?.Name ?? "";
            default:
                return "";
        }
    }

    /// <summary>
    /// Substitutes {Token} placeholders in a formula string with element values.
    /// Token forms:
    ///   {ParamName}            instance parameter
    ///   {[Type] ParamName}     type parameter (with literal "[Type] " prefix)
    ///   {ElementId|Category|Family|Type}  built-in fields
    /// Anything outside braces is appended literally.
    /// </summary>
    private static string EvaluateFormula(Element elem, ElementType? typeElem, string formula)
    {
        if (string.IsNullOrEmpty(formula)) return "";
        var sb = new StringBuilder();
        int i = 0;
        while (i < formula.Length)
        {
            char c = formula[i];
            if (c == '{')
            {
                int end = formula.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(formula.Substring(i)); break; }
                var token = formula.Substring(i + 1, end - i - 1).Trim();
                sb.Append(ResolveToken(elem, typeElem, token));
                i = end + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string ResolveToken(Element elem, ElementType? typeElem, string token)
    {
        if (string.IsNullOrEmpty(token)) return "";
        // Built-in fields
        switch (token)
        {
            case "ElementId":
            case "Category":
            case "Family":
            case "Type":
                return ResolveBuiltInField(elem, typeElem, token);
        }
        // [Type] prefix → type parameter
        if (token.StartsWith("[Type]", StringComparison.OrdinalIgnoreCase))
        {
            var name = token.Substring("[Type]".Length).Trim();
            var p = typeElem?.LookupParameter(name);
            return p != null ? GetParamDisplayValue(p) : "";
        }
        // Default: instance parameter
        var ip = elem.LookupParameter(token);
        return ip != null ? GetParamDisplayValue(ip) : "";
    }

    /// <summary>
    /// Stringifies a Parameter value for CSV output. Numeric fallbacks use
    /// <see cref="CultureInfo.InvariantCulture"/> so a locale with comma
    /// decimal separator (e.g. it-IT) does not produce "1,5" that Power BI
    /// — reading with Delimiter="," — would split into two columns.
    /// Note: <c>AsValueString()</c> still returns Revit's locale-formatted
    /// display string (e.g. "1500 mm"); the invariant-culture pass only
    /// kicks in when Revit can't format the value itself.
    /// </summary>
    private static string GetParamDisplayValue(Parameter p)
    {
        if (!p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("F4", CultureInfo.InvariantCulture),
            StorageType.ElementId => p.AsValueString() ?? p.AsElementId().ToString(),
            _ => ""
        };
    }

    /// <summary>
    /// Writes a CSV atomically: first to a sibling .tmp file, then renames
    /// onto the target path with overwrite. This prevents Power BI scheduled
    /// refresh from reading a half-written file when the export coincides
    /// with the refresh window.
    /// </summary>
    private static void WriteCsvAtomic(string targetPath, string content)
    {
        var tmpPath = targetPath + ".tmp";
        File.WriteAllText(tmpPath, content, Encoding.UTF8);
        // File.Move with overwrite is atomic on NTFS — safe even if Power BI
        // is reading targetPath right now (the open handle keeps the old file
        // alive until released; the new write replaces the entry).
        if (File.Exists(targetPath)) File.Delete(targetPath);
        File.Move(tmpPath, targetPath);
    }

    private class ColumnTypeDef
    {
        public string PbiType { get; set; } = "auto";
        public string? Format { get; set; }
        public bool IsNumeric => PbiType is "int" or "number" or "fixed" or "percent";
        public bool IsAuto => string.Equals(PbiType, "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ColumnTypeDef> ParseColumnTypes(JArray? arr)
    {
        var dict = new Dictionary<string, ColumnTypeDef>(StringComparer.OrdinalIgnoreCase);
        if (arr == null) return dict;
        foreach (var item in arr.OfType<JObject>())
        {
            var name = item["ColumnName"]?.ToString() ?? item["columnName"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) continue;
            dict[name] = new ColumnTypeDef
            {
                PbiType = item["PbiType"]?.ToString() ?? item["pbiType"]?.ToString() ?? "auto",
                Format = item["Format"]?.ToString() ?? item["format"]?.ToString()
            };
        }
        return dict;
    }

    private static bool IsNumericMapped(Dictionary<string, ColumnTypeDef> map, string colName)
        => map.TryGetValue(colName, out var def) && def.IsNumeric;

    /// <summary>
    /// Raw invariant-culture value for a parameter, used to fill <c>&lt;col&gt;_Raw</c>
    /// companion columns when the user maps a column to a numeric Power BI type.
    /// </summary>
    private static string GetParamRawValue(Parameter p)
    {
        if (!p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double => p.AsDouble().ToString("G", CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    private static string MapToPowerQueryType(string pbiType) => (pbiType ?? "auto").ToLowerInvariant() switch
    {
        "int"      => "Int64.Type",
        "number"   => "type number",
        "fixed"    => "Currency.Type",
        "percent"  => "Percentage.Type",
        "bool"     => "type logical",
        "date"     => "type date",
        "datetime" => "type datetime",
        "duration" => "type duration",
        _          => "type text"
    };

    /// <summary>
    /// Writes a Power Query (.pq) sidecar that PBI can load via 'Get Data →
    /// Blank Query → Advanced Editor'. For numeric-typed columns we type the
    /// <c>_Raw</c> companion (the invariant-culture value), leaving the display
    /// column as text so the locale-formatted "1500 mm" string remains usable
    /// for labels. Auto-typed columns are skipped (PBI auto-infers them).
    /// </summary>
    private static void WritePowerQuerySidecar(string csvPath, List<string> headers, Dictionary<string, ColumnTypeDef> columnTypes)
    {
        var transforms = new List<string>();
        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);

        foreach (var h in headers)
        {
            // _Raw companion: type as the numeric PBI type.
            if (h.EndsWith("_Raw", StringComparison.Ordinal))
            {
                var baseName = h.Substring(0, h.Length - 4);
                if (columnTypes.TryGetValue(baseName, out var defR) && !defR.IsAuto)
                {
                    transforms.Add($"        {{\"{EscapeForPq(h)}\", {MapToPowerQueryType(defR.PbiType)}}}");
                }
                continue;
            }
            // Display column: if it has a _Raw companion (i.e. is a mapped numeric
            // column) leave it text. Otherwise emit the user-chosen type.
            if (columnTypes.TryGetValue(h, out var def) && !def.IsAuto)
            {
                if (def.IsNumeric && headerSet.Contains(h + "_Raw"))
                    transforms.Add($"        {{\"{EscapeForPq(h)}\", type text}}");
                else
                    transforms.Add($"        {{\"{EscapeForPq(h)}\", {MapToPowerQueryType(def.PbiType)}}}");
            }
        }

        if (transforms.Count == 0) return; // nothing to emit

        var transformBlock = string.Join(",\r\n", transforms);
        var fullCsv = csvPath.Replace("\\", "\\\\");
        var pq = $@"// Auto-generated by RevitCortex push_to_powerbi.
// Load in Power BI Desktop: Get Data → Blank Query → Advanced Editor → paste this.
// For scheduled refresh, point the dataset at this query (it embeds the CSV path).
let
    Source = Csv.Document(File.Contents(""{fullCsv}""), [Delimiter="","", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),
    Promoted = Table.PromoteHeaders(Source, [PromoteAllScalars=true]),
    Typed = Table.TransformColumnTypes(Promoted, {{
{transformBlock}
    }})
in
    Typed
";

        var pqPath = Path.ChangeExtension(csvPath, ".pq");
        File.WriteAllText(pqPath, pq, Encoding.UTF8);
    }

    private static string EscapeForPq(string columnName)
        => columnName.Replace("\\", "\\\\").Replace("\"", "\"\"");

    private static string ToCsvRow(IEnumerable<string> fields) =>
        string.Join(",", fields);

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private class LinkedHashSet
    {
        private readonly List<string> _items = new();
        private readonly HashSet<string> _set = new();
        public IReadOnlyList<string> Items => _items;
        public int Count => _items.Count;
        public void Add(string item) { if (_set.Add(item)) _items.Add(item); }
        public void Remove(string item) { if (_set.Remove(item)) _items.Remove(item); }
    }
}
