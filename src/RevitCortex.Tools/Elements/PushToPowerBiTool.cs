using System;
using System.Collections.Generic;
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

            // Discover parameter columns (sample first 100 for performance)
            var instanceParamNames = new LinkedHashSet();
            var typeParamNames = new LinkedHashSet();
            var typeCache = new Dictionary<ElementId, Element?>();

            foreach (var elem in elemList.Take(100))
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (parameterNames.Count == 0 || parameterNames.Contains(p.Definition.Name))
                        instanceParamNames.Add(p.Definition.Name);
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
                                if (parameterNames.Count == 0 || parameterNames.Contains(p.Definition.Name))
                                    typeParamNames.Add(p.Definition.Name);
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
                var header = new List<string> { "ElementId", "Category", "Family", "Type" };
                header.AddRange(instanceParamNames.Items);
                if (includeTypeParams)
                    header.AddRange(typeParamNames.Items.Select(n => $"[Type] {n}"));
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
                        ToolHelpers.GetElementIdValue(elem.Id).ToString(),
                        CsvEscape(elem.Category?.Name ?? ""),
                        CsvEscape(elem.LookupParameter("Family Name")?.AsString()
                            ?? (typeElem as ElementType)?.FamilyName ?? ""),
                        CsvEscape(elem.LookupParameter("Type Name")?.AsValueString()
                            ?? (typeElem as ElementType)?.Name ?? "")
                    };

                    foreach (var name in instanceParamNames.Items)
                    {
                        var p = elem.LookupParameter(name);
                        row.Add(CsvEscape(p != null ? GetParamDisplayValue(p) : ""));
                    }

                    if (includeTypeParams)
                    {
                        foreach (var name in typeParamNames.Items)
                        {
                            var p = typeElem?.LookupParameter(name);
                            row.Add(CsvEscape(p != null ? GetParamDisplayValue(p) : ""));
                        }
                    }

                    sb.AppendLine(ToCsvRow(row));
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            // Write metadata sidecar
            var meta = new JObject
            {
                ["refreshed_at"] = DateTime.UtcNow.ToString("o"),
                ["document"] = doc.Title,
                ["element_count"] = elemList.Count,
                ["categories"] = categories.Count > 0 ? string.Join(", ", categories) : "all",
                ["file"] = fileName,
                ["column_count"] = columnCount,
                ["mode"] = columnMappingsRaw != null && columnMappingsRaw.Count > 0 ? "mapping" : "discovery"
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

                // ElementId is always the first column — required for Elements ↔ Schedule join in PBI
                var headerCells = new List<string> { "ElementId" };
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

                    var row = new List<string>(fieldHeadings.Count + 1)
                    {
                        ToolHelpers.GetElementIdValue(elem.Id).ToString(System.Globalization.CultureInfo.InvariantCulture)
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

                File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
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

        // Metadata sidecar
        var meta = new JObject
        {
            ["refreshed_at"] = DateTime.UtcNow.ToString("o"),
            ["document"] = doc.Title,
            ["mode"] = "schedule",
            ["schedule_count"] = scheduleIds.Count,
            ["total_rows"] = totalRows
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

    private static string GetParamDisplayValue(Parameter p)
    {
        if (!p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Integer => p.AsInteger().ToString(),
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("F4"),
            StorageType.ElementId => p.AsValueString() ?? p.AsElementId().ToString(),
            _ => ""
        };
    }

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
