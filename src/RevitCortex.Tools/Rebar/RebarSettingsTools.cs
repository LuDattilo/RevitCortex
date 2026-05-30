using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Rebar;

// ─────────────────────────────────────────────────────────────────────────────
// Module 6 — reinforcement settings, rebar/fabric rounding, numbering, bending
// details.
//
// API verified by reflection across RevitAPI ref DLLs 2023.1.90 / 2024.3.40 /
// 2025.4.50 / 2026.4.10 / 2027.0.20:
//   • ReinforcementSettings.GetReinforcementSettings(Document) is static; the
//     writable toggles HostStructuralRebar / RebarShapeDefinesHooks /
//     RebarShapeDefinesEndTreatments all exist R23+ (the last two read
//     defensively because some toggles only apply when the doc has no rebar).
//   • Rounding managers are RebarRoundingManager / FabricRoundingManager (both
//     derive from ReinforcementRoundingManager). The real writable surface is
//     IsActiveOnElement (apply-rules toggle), SegmentLengthRounding +
//     SegmentLengthRoundingMethod, TotalLengthRounding + TotalLengthRoundingMethod
//     (all RoundingMethod = Nearest|Up|Down). There is NO volume-rounding member,
//     so a provided volumeRounding field is surfaced as a warning, never silently
//     dropped. Element-level: Rebar.GetReinforcementRoundingManager(); document
//     defaults: ReinforcementSettings.GetRebarRoundingManager() /
//     GetFabricRoundingManager().
//   • Numbering: there is no NumberingSchema / RebarNumberingSchema class on any
//     version. set_number writes Rebar.ScheduleMark (String, writable R23+);
//     renumber / remove_gaps have no public API and return a structured Fail.
//   • Bending details (2024+): RebarBendingDetail is a static-method API; the
//     create signature (doc, viewId, reinforcementElementId, subelementKey,
//     RebarBendingDetailType, position, rotation) is stable R24→R27. The
//     RebarBendingDetailType factory diverges by version (Create on R24/R25 only;
//     CreateSchematic/CreateRealistic R25→R27), so a type is resolved from the
//     document (by id or first available) for portability; if none exists we Fail
//     rather than call a version-specific factory.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Sets document-level reinforcement settings. Only provided fields are changed.</summary>
public class SetReinforcementSettingsTool : ICortexTool
{
    public string Name => "set_reinforcement_settings";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set document-level reinforcement settings. Optional fields: hostStructuralRebar (bool), rebarShapeDefinesHooks (bool), rebarShapeDefinesEndTreatments (bool). Some toggles are only allowed when the document has no reinforcement.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var setHost = input["hostStructuralRebar"]?.Value<bool?>();
        var setDefinesHooks = input["rebarShapeDefinesHooks"]?.Value<bool?>();
        var setDefinesEndTreatments = input["rebarShapeDefinesEndTreatments"]?.Value<bool?>();
        if (setHost == null && setDefinesHooks == null && setDefinesEndTreatments == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Provide at least one setting to change");

        if (!session.RequestConfirmation("change reinforcement settings", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Reinforcement Settings");
        tx.Start();
        var warnings = new List<string>();
        try
        {
            var s = ReinforcementSettings.GetReinforcementSettings(doc!);
            if (setHost != null) s.HostStructuralRebar = setHost.Value;
            if (setDefinesHooks != null)
            {
                try { s.RebarShapeDefinesHooks = setDefinesHooks.Value; }
                catch (Exception ex) { warnings.Add($"rebarShapeDefinesHooks not changed: {ex.Message}"); }
            }
            if (setDefinesEndTreatments != null)
            {
                try { s.RebarShapeDefinesEndTreatments = setDefinesEndTreatments.Value; }
                catch (Exception ex) { warnings.Add($"rebarShapeDefinesEndTreatments not changed: {ex.Message}"); }
            }
            tx.Commit();
            return CortexResult<object>.Ok(new { changed = true, warnings });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set reinforcement settings: {ex.Message}");
        }
    }
}

/// <summary>
/// Shared helpers for the rebar/fabric rounding managers. The two managers
/// (RebarRoundingManager / FabricRoundingManager) expose an identical writable
/// surface (verified R23-R27), so both write and read paths reuse this code.
/// </summary>
internal static class RoundingManagerHelper
{
    /// <summary>Maps a Nearest|Up|Down string to the RoundingMethod enum.</summary>
    public static bool TryParseMethod(string? raw, out RoundingMethod method, out string? err)
    {
        method = RoundingMethod.Nearest;
        err = null;
        if (string.IsNullOrWhiteSpace(raw)) { err = "lengthRoundingMethod is empty"; return false; }
        switch (raw!.Trim().ToLowerInvariant())
        {
            case "nearest": method = RoundingMethod.Nearest; return true;
            case "up": method = RoundingMethod.Up; return true;
            case "down": method = RoundingMethod.Down; return true;
            default:
                err = $"Invalid lengthRoundingMethod '{raw}'. Valid values: Nearest, Up, Down";
                return false;
        }
    }

    /// <summary>Serialises a rounding manager (rebar or fabric) to a DTO in mm.</summary>
    public static JObject ToDto(dynamic mgr, string scope)
    {
        // RebarRoundingManager and FabricRoundingManager share these members.
        var dto = new JObject
        {
            ["scope"] = scope,
            ["applyRoundingRules"] = (bool)mgr.IsActiveOnElement,
            ["segmentLengthRoundingMm"] = RebarToolHelpers.ToMm((double)mgr.SegmentLengthRounding),
            ["segmentLengthRoundingMethod"] = ((RoundingMethod)mgr.SegmentLengthRoundingMethod).ToString(),
            ["totalLengthRoundingMm"] = RebarToolHelpers.ToMm((double)mgr.TotalLengthRounding),
            ["totalLengthRoundingMethod"] = ((RoundingMethod)mgr.TotalLengthRoundingMethod).ToString(),
            // Applicable* values are the effective rounding actually used (element vs document default).
            ["applicableSegmentLengthRoundingMm"] = RebarToolHelpers.ToMm((double)mgr.ApplicableSegmentLengthRounding),
            ["applicableTotalLengthRoundingMm"] = RebarToolHelpers.ToMm((double)mgr.ApplicableTotalLengthRounding),
            ["roundingSource"] = ((object)mgr.ApplicableReinforcementRoundingSource).ToString()
        };
        return dto;
    }

    /// <summary>
    /// Applies the provided rounding fields to a manager inside an open transaction.
    /// Returns a Fail result on bad/empty input, otherwise null. Any unsupported field
    /// (e.g. volumeRounding) is added to <paramref name="warnings"/>, never silently dropped.
    /// </summary>
    public static CortexResult<object>? Apply(dynamic mgr, JObject input, List<string> warnings)
    {
        bool any = false;

        var applyRules = input["applyRules"]?.Value<bool?>();
        if (applyRules != null) { mgr.IsActiveOnElement = applyRules.Value; any = true; }

        // lengthRoundingMm / lengthRoundingMethod map to the segment-length rounding,
        // which is the per-bar (cut-length) rounding shown in schedules.
        var lengthMm = input["lengthRoundingMm"]?.Value<double?>();
        if (lengthMm != null) { mgr.SegmentLengthRounding = RebarToolHelpers.FromMm(lengthMm.Value); any = true; }

        var methodRaw = input["lengthRoundingMethod"]?.Value<string>();
        if (methodRaw != null)
        {
            if (!TryParseMethod(methodRaw, out RoundingMethod m, out var merr))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, merr!);
            mgr.SegmentLengthRoundingMethod = m;
            any = true;
        }

        // The Revit API has no volume-rounding member on the rounding managers
        // (verified R23-R27). Surface the request instead of dropping it silently.
        if (input["volumeRounding"] != null || input["volumeRoundingMm"] != null)
            warnings.Add("volumeRounding is not supported by the Revit reinforcement rounding API; ignored");

        if (!any)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide at least one of: applyRules, lengthRoundingMm, lengthRoundingMethod");

        return null;
    }
}

/// <summary>Reads the rebar length-rounding configuration (document default or element override).</summary>
public class GetRebarRoundingTool : ICortexTool
{
    public string Name => "get_rebar_rounding";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read rebar length-rounding rules. Without rebarId returns the document default; with rebarId returns that bar's effective rounding manager. RoundingMethod is one of Nearest/Up/Down. There is no volume rounding in the Revit API.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var rebarId = input["rebarId"]?.Value<long?>();
        try
        {
            if (rebarId != null && rebarId > 0)
            {
                var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, rebarId);
                if (rerr != null) return rerr;
                var mgr = rebar!.GetReinforcementRoundingManager();
                return CortexResult<object>.Ok(RoundingManagerHelper.ToDto(mgr, "element"));
            }
            var docMgr = ReinforcementSettings.GetReinforcementSettings(doc!).GetRebarRoundingManager();
            return CortexResult<object>.Ok(RoundingManagerHelper.ToDto(docMgr, "document"));
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar rounding: {ex.Message}");
        }
    }
}

/// <summary>Reads the document fabric length-rounding configuration.</summary>
public class GetFabricRoundingTool : ICortexTool
{
    public string Name => "get_fabric_rounding";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the document fabric length-rounding rules (apply flag, segment/total length rounding in mm and method). RoundingMethod is one of Nearest/Up/Down.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var mgr = ReinforcementSettings.GetReinforcementSettings(doc!).GetFabricRoundingManager();
            return CortexResult<object>.Ok(RoundingManagerHelper.ToDto(mgr, "document"));
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read fabric rounding: {ex.Message}");
        }
    }
}

/// <summary>Sets rebar length-rounding rules (document default or element override).</summary>
public class ManageRebarRoundingTool : ICortexTool
{
    public string Name => "manage_rebar_rounding";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set rebar length-rounding rules. Without rebarId edits the document default; with rebarId edits that bar's manager. Fields: applyRules (bool), lengthRoundingMm (double), lengthRoundingMethod (Nearest|Up|Down). volumeRounding is unsupported by the API and reported in warnings.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var rebarId = input["rebarId"]?.Value<long?>();

        // Resolve the manager target before confirmation so we fail fast on bad ids.
        dynamic mgr;
        string scope;
        try
        {
            if (rebarId != null && rebarId > 0)
            {
                var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, rebarId);
                if (rerr != null) return rerr;
                mgr = rebar!.GetReinforcementRoundingManager();
                scope = "element";
            }
            else
            {
                mgr = ReinforcementSettings.GetReinforcementSettings(doc!).GetRebarRoundingManager();
                scope = "document";
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to resolve rebar rounding manager: {ex.Message}");
        }

        // Validate inputs (parsing) before opening a transaction / confirmation.
        var methodRaw = input["lengthRoundingMethod"]?.Value<string>();
        if (methodRaw != null && !RoundingManagerHelper.TryParseMethod(methodRaw, out _, out var merr))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, merr!);

        if (!session.RequestConfirmation("change rebar rounding rules", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Manage Rebar Rounding");
        tx.Start();
        var warnings = new List<string>();
        try
        {
            var bad = RoundingManagerHelper.Apply(mgr, input, warnings);
            if (bad != null) { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); return bad; }
            tx.Commit();
            JObject dto = RoundingManagerHelper.ToDto(mgr, scope);
            dto["warnings"] = JArray.FromObject(warnings);
            return CortexResult<object>.Ok(dto);
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set rebar rounding: {ex.Message}");
        }
    }
}

/// <summary>Sets the document fabric length-rounding rules.</summary>
public class ManageFabricRoundingTool : ICortexTool
{
    public string Name => "manage_fabric_rounding";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the document fabric length-rounding rules. Fields: applyRules (bool), lengthRoundingMm (double), lengthRoundingMethod (Nearest|Up|Down). volumeRounding is unsupported by the API and reported in warnings.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var methodRaw = input["lengthRoundingMethod"]?.Value<string>();
        if (methodRaw != null && !RoundingManagerHelper.TryParseMethod(methodRaw, out _, out var merr))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, merr!);

        if (!session.RequestConfirmation("change fabric rounding rules", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Manage Fabric Rounding");
        tx.Start();
        var warnings = new List<string>();
        try
        {
            dynamic mgr = ReinforcementSettings.GetReinforcementSettings(doc!).GetFabricRoundingManager();
            var bad = RoundingManagerHelper.Apply(mgr, input, warnings);
            if (bad != null) { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); return bad; }
            tx.Commit();
            JObject dto = RoundingManagerHelper.ToDto(mgr, "document");
            dto["warnings"] = JArray.FromObject(warnings);
            return CortexResult<object>.Ok(dto);
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set fabric rounding: {ex.Message}");
        }
    }
}

/// <summary>Reads rebar numbering / schedule marks (single bar or category-wide).</summary>
public class GetRebarNumberingTool : ICortexTool
{
    public string Name => "get_rebar_numbering";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read rebar numbering. With rebarId returns that bar's schedule mark; without it returns every Rebar's schedule mark plus the count of empty/blank marks (a proxy for numbering gaps). The document-wide list is capped by maxResults (default 100; truncated/returnedCount flag a cut) and summaryOnly (default false) returns only count + blankMarkCount. Schedule marks are the 'Rebar Number' shown in schedules.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var rebarId = input["rebarId"]?.Value<long?>();
        try
        {
            if (rebarId != null && rebarId > 0)
            {
                var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, rebarId);
                if (rerr != null) return rerr;
                return CortexResult<object>.Ok(new
                {
                    rebarId = ToolHelpers.GetElementIdValue(rebar!),
                    scheduleMark = SafeScheduleMark(rebar!)
                });
            }

            var maxResults = input["maxResults"]?.Value<int?>() ?? 100;
            var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;

            var bars = new FilteredElementCollector(doc!)
                .OfClass(typeof(Autodesk.Revit.DB.Structure.Rebar))
                .Cast<Autodesk.Revit.DB.Structure.Rebar>()
                .ToList();

            // count and blankMarkCount are computed over ALL rebars and stay truthful
            // even when the rebars[] list below is capped (response-shaping contract:
            // counters are never derived from a trimmed list).
            int blank = 0;
            var items = new List<JObject>();
            foreach (var r in bars)
            {
                var mark = SafeScheduleMark(r);
                if (string.IsNullOrWhiteSpace(mark)) blank++;
                if (!summaryOnly && items.Count < maxResults)
                {
                    items.Add(new JObject
                    {
                        ["rebarId"] = ToolHelpers.GetElementIdValue(r),
                        ["scheduleMark"] = mark
                    });
                }
            }
            int count = bars.Count;

            if (summaryOnly)
            {
                return CortexResult<object>.Ok(new
                {
                    count,
                    blankMarkCount = blank,
                    summaryOnly = true,
                    note = "blankMarkCount approximates numbering gaps; Revit has no public rebar-numbering-schema API."
                });
            }

            return CortexResult<object>.Ok(new
            {
                count,
                blankMarkCount = blank,
                returnedCount = items.Count,
                truncated = count > maxResults,
                rebars = items,
                note = "blankMarkCount approximates numbering gaps; Revit has no public rebar-numbering-schema API."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar numbering: {ex.Message}");
        }
    }

    private static string? SafeScheduleMark(Autodesk.Revit.DB.Structure.Rebar r)
    {
        try { return r.ScheduleMark; } catch { return null; }
    }
}

/// <summary>Manages rebar numbering: set a schedule mark, or report that renumber/remove-gaps have no API.</summary>
public class ManageRebarNumberingTool : ICortexTool
{
    public string Name => "manage_rebar_numbering";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Manage rebar numbering. action=set_number writes a single bar's schedule mark (rebarId + newNumber). action=renumber|remove_gaps are not exposed by the Revit API and return a structured 'unsupported' result. Setting a number may be read-only on some targets — that is surfaced in warnings rather than failing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var action = (input["action"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
        switch (action)
        {
            case "set_number":
                return SetNumber(doc!, input, session);
            case "renumber":
            case "remove_gaps":
                // Verified R23-R27: no public rebar numbering-schema / partition API exists.
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"action '{action}' is not supported: the Revit API exposes no rebar numbering-schema (renumber/remove-gaps) method on any version.",
                    suggestion: "Use action=set_number to assign individual schedule marks, or renumber via the Revit UI.");
            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Invalid 'action'. Valid values: set_number, renumber, remove_gaps");
        }
    }

    private static CortexResult<object> SetNumber(Document doc, JObject input, CortexSession session)
    {
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        // newNumber accepts a string mark or a number; the underlying ScheduleMark is a string.
        var token = input["newNumber"];
        if (token == null || token.Type == JTokenType.Null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "newNumber is required for action=set_number");
        var newMark = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();

        if (!session.RequestConfirmation("set rebar number", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Rebar Number");
        tx.Start();
        var warnings = new List<string>();
        try
        {
            bool applied = false;
            try
            {
                rebar!.ScheduleMark = newMark;
                applied = true;
            }
            catch (Exception ex)
            {
                // On some targets the schedule mark / REBAR_NUMBER is read-only — surface, don't fail.
                warnings.Add($"Could not set the rebar number on this Revit version (may be read-only): {ex.Message}");
            }
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar!),
                requestedNumber = newMark,
                scheduleMark = SafeMark(rebar!),
                applied,
                warnings
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set rebar number: {ex.Message}");
        }
    }

    private static string? SafeMark(Autodesk.Revit.DB.Structure.Rebar r)
    {
        try { return r.ScheduleMark; } catch { return null; }
    }
}

/// <summary>Creates a rebar bending detail in a view (Revit 2024+).</summary>
public class CreateRebarBendingDetailTool : ICortexTool
{
    public string Name => "create_rebar_bending_detail";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a rebar bending detail for a rebar in a view (Revit 2024+). Provide rebarId and viewId (a drafting/detail view); optional bendingDetailTypeId, position JSON {x,y,z} in mm, rotationDegrees. Returns a version error on Revit 2023.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2024_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        var viewId = input["viewId"]?.Value<long?>();
        if (viewId == null || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewId is required (a drafting/detail view to host the bending detail)");
        var view = doc!.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
        if (view == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No view with id {viewId}");

        // Resolve a RebarBendingDetailType portably. The factory diverges by version
        // (Create on R24/R25; CreateSchematic/CreateRealistic R25-R27), so we resolve
        // an existing type from the document instead of calling a version-specific factory.
        RebarBendingDetailType? detailType = null;
        var typeId = input["bendingDetailTypeId"]?.Value<long?>();
        if (typeId != null && typeId > 0)
        {
            detailType = doc.GetElement(ToolHelpers.ToElementId(typeId.Value)) as RebarBendingDetailType;
            if (detailType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No RebarBendingDetailType with id {typeId}");
        }
        else
        {
            detailType = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBendingDetailType))
                .Cast<RebarBendingDetailType>()
                .FirstOrDefault();
            if (detailType == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No RebarBendingDetailType exists in this document.",
                    suggestion: "Create a rebar bending detail type in the Revit UI (or load a structural template) and pass its id as bendingDetailTypeId.");
        }

        var position = input["position"] != null ? RebarToolHelpers.ParseXyzMm(input["position"]!) : XYZ.Zero;
        var rotation = (input["rotationDegrees"]?.Value<double?>() ?? 0.0) * Math.PI / 180.0;

        if (!session.RequestConfirmation("create rebar bending detail", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Rebar Bending Detail");
        tx.Start();
        var warnings = new List<string>();
        try
        {
            // The reinforcement subelement key 0 targets the rebar element itself.
            var detail = RebarBendingDetail.Create(
                doc, view.Id, rebar!.Id, 0, detailType, position, rotation);
            if (detail == null)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Revit returned no bending detail; the rebar may not support a bending detail in this view.");
            }
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                bendingDetailId = ToolHelpers.GetElementIdValue(detail),
                rebarId = ToolHelpers.GetElementIdValue(rebar!),
                viewId = ToolHelpers.GetElementIdValue(view),
                bendingDetailTypeId = ToolHelpers.GetElementIdValue(detailType),
                warnings
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create rebar bending detail: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar bending details", 2024);
#endif
    }
}

/// <summary>Modifies a rebar bending detail's position/rotation (Revit 2024+).</summary>
public class ModifyRebarBendingDetailTool : ICortexTool
{
    public string Name => "modify_rebar_bending_detail";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Modify a rebar bending detail (Revit 2024+). Provide bendingDetailId and any of: position JSON {x,y,z} in mm, rotationDegrees. Returns a version error on Revit 2023.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2024_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var detailId = input["bendingDetailId"]?.Value<long?>();
        if (detailId == null || detailId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "bendingDetailId is required");
        var detail = doc!.GetElement(ToolHelpers.ToElementId(detailId.Value));
        if (detail == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {detailId}");
        if (!RebarBendingDetail.IsBendingDetail(detail))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Element {detailId} is not a rebar bending detail");

        bool hasPosition = input["position"] != null;
        var rotationDeg = input["rotationDegrees"]?.Value<double?>();
        if (!hasPosition && rotationDeg == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Provide at least one of: position, rotationDegrees");

        if (!session.RequestConfirmation("modify rebar bending detail", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Modify Rebar Bending Detail");
        tx.Start();
        try
        {
            if (hasPosition)
                RebarBendingDetail.SetPosition(detail, RebarToolHelpers.ParseXyzMm(input["position"]!));
            if (rotationDeg != null)
                RebarBendingDetail.SetRotation(detail, rotationDeg.Value * Math.PI / 180.0);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                bendingDetailId = ToolHelpers.GetElementIdValue(detail),
                positionMm = RebarToolHelpers.XyzToDtoMm(RebarBendingDetail.GetPosition(detail)),
                rotationDegrees = RebarBendingDetail.GetRotation(detail) * 180.0 / Math.PI
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to modify rebar bending detail: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar bending details", 2024);
#endif
    }
}

/// <summary>Reads a rebar bending detail's host/view/position data (Revit 2024+).</summary>
public class GetRebarBendingDetailDataTool : ICortexTool
{
    public string Name => "get_rebar_bending_detail_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a rebar bending detail (Revit 2024+): host rebar id, owner view id, position (mm) and rotation (degrees). Provide bendingDetailId. Returns a version error on Revit 2023.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2024_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var detailId = input["bendingDetailId"]?.Value<long?>();
        if (detailId == null || detailId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "bendingDetailId is required");
        var detail = doc!.GetElement(ToolHelpers.ToElementId(detailId.Value));
        if (detail == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {detailId}");
        if (!RebarBendingDetail.IsBendingDetail(detail))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Element {detailId} is not a rebar bending detail");

        try
        {
            long hostRebarId = -1;
            try
            {
                var hostRef = RebarBendingDetail.GetHost(detail);
                if (hostRef != null) hostRebarId = ToolHelpers.GetElementIdValue(hostRef.ElementId);
            }
            catch { /* host reference may be unavailable; reported as -1 */ }

            return CortexResult<object>.Ok(new
            {
                bendingDetailId = ToolHelpers.GetElementIdValue(detail),
                hostRebarId,
                ownerViewId = ToolHelpers.GetElementIdValue(detail.OwnerViewId),
                positionMm = RebarToolHelpers.XyzToDtoMm(RebarBendingDetail.GetPosition(detail)),
                rotationDegrees = RebarBendingDetail.GetRotation(detail) * 180.0 / Math.PI
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar bending detail: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar bending details", 2024);
#endif
    }
}
