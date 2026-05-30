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

/// <summary>
/// Creates an area reinforcement system on a host. Either covers the host boundary (no curves) or
/// uses an explicit boundary (curves[] in mm). Major direction is mandatory and fixed at creation.
/// </summary>
public class CreateAreaReinforcementTool : ICortexTool
{
    public string Name => "create_area_reinforcement";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create an area reinforcement system on a host (wall/floor/foundation). Provide hostId, majorDirection{x,y,z}, barTypeId|barTypeName; optional curves[] (mm) for an explicit boundary, areaTypeId, hookTypeId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved",
            suggestion: "Use list_rebar_bar_types to find a barTypeId");
        if (input["majorDirection"] == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "majorDirection{x,y,z} is required");
        var major = RebarToolHelpers.ParseXyzMm(input["majorDirection"]!).Normalize();

        // AreaReinforcementType: prefer an explicit id, then any existing type, then create the default.
        var areaType = doc!.GetElement(ToolHelpers.ToElementId(input["areaTypeId"]?.Value<long?>() ?? -1)) as AreaReinforcementType
            ?? new FilteredElementCollector(doc).OfClass(typeof(AreaReinforcementType)).Cast<AreaReinforcementType>().FirstOrDefault();
        var areaTypeId = areaType?.Id ?? AreaReinforcementType.CreateDefaultAreaReinforcementType(doc);
        if (areaTypeId == null || areaTypeId == ElementId.InvalidElementId)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No AreaReinforcementType could be resolved or created in document");

        var hookId = ElementId.InvalidElementId;
        var hook = RebarToolHelpers.ResolveRebarHookType(doc, input["hookTypeId"]?.Value<long?>(), input["hookTypeName"]?.Value<string>());
        if (hook != null) hookId = hook.Id;

        IList<Curve>? curves = null;
        if (input["curves"] is JArray ca)
        {
            curves = RebarToolHelpers.ParseCurveSpecsMm(ca, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
        }

        if (!session.RequestConfirmation("create area reinforcement", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Area Reinforcement");
        tx.Start();
        try
        {
            // Verified across R23-R27: AreaReinforcement.Create overloads are
            //   (Document, Element host, XYZ major, ElementId areaTypeId, ElementId barTypeId, ElementId hookId) and
            //   (Document, Element host, IList<Curve> curves, XYZ major, ElementId areaTypeId, ElementId barTypeId, ElementId hookId).
            AreaReinforcement area = curves != null
                ? AreaReinforcement.Create(doc, host!, curves, major, areaTypeId, barType.Id, hookId)
                : AreaReinforcement.Create(doc, host!, major, areaTypeId, barType.Id, hookId);
            if (area == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no area reinforcement"); }
            var memberIds = area.GetRebarInSystemIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var id = ToolHelpers.GetElementIdValue(area);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created area reinforcement {id} with {memberIds.Count} bar system member(s)",
                areaReinforcementId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                memberCount = memberIds.Count,
                memberIds
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create area reinforcement: {ex.Message}");
        }
    }
}

/// <summary>
/// Creates a path reinforcement system on a host edge. Requires an explicit boundary (curves[] in mm).
/// 'flip' chooses the side; major bar direction follows the path.
/// </summary>
public class CreatePathReinforcementTool : ICortexTool
{
    public string Name => "create_path_reinforcement";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a path reinforcement system on a host. Provide hostId, curves[] (mm, required), barTypeId|barTypeName; optional flip (bool), pathTypeId, startHookId, endHookId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved",
            suggestion: "Use list_rebar_bar_types to find a barTypeId");
        if (!(input["curves"] is JArray ca))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "curves array is required for path reinforcement");
        var curves = RebarToolHelpers.ParseCurveSpecsMm(ca, out var cerr);
        if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);

        var flip = input["flip"]?.Value<bool?>() ?? false;

        // PathReinforcementType: prefer an explicit id, then any existing type, then create the default.
        var pathType = doc!.GetElement(ToolHelpers.ToElementId(input["pathTypeId"]?.Value<long?>() ?? -1)) as PathReinforcementType
            ?? new FilteredElementCollector(doc).OfClass(typeof(PathReinforcementType)).Cast<PathReinforcementType>().FirstOrDefault();
        var pathTypeId = pathType?.Id ?? PathReinforcementType.CreateDefaultPathReinforcementType(doc);
        if (pathTypeId == null || pathTypeId == ElementId.InvalidElementId)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No PathReinforcementType could be resolved or created in document");

        var startHookId = ElementId.InvalidElementId;
        var startHook = RebarToolHelpers.ResolveRebarHookType(doc, input["startHookId"]?.Value<long?>(), input["startHookName"]?.Value<string>());
        if (startHook != null) startHookId = startHook.Id;
        var endHookId = ElementId.InvalidElementId;
        var endHook = RebarToolHelpers.ResolveRebarHookType(doc, input["endHookId"]?.Value<long?>(), input["endHookName"]?.Value<string>());
        if (endHook != null) endHookId = endHook.Id;

        if (!session.RequestConfirmation("create path reinforcement", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Path Reinforcement");
        tx.Start();
        try
        {
            // Verified across R23-R27: PathReinforcement.Create(Document, Element host, IList<Curve> curves,
            //   bool flip, ElementId pathTypeId, ElementId barTypeId, ElementId startHookId, ElementId endHookId).
            PathReinforcement path = PathReinforcement.Create(doc, host!, curves, flip, pathTypeId, barType.Id, startHookId, endHookId);
            if (path == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no path reinforcement"); }
            var memberIds = path.GetRebarInSystemIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var id = ToolHelpers.GetElementIdValue(path);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created path reinforcement {id} with {memberIds.Count} bar system member(s)",
                pathReinforcementId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                flip,
                memberCount = memberIds.Count,
                memberIds
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create path reinforcement: {ex.Message}");
        }
    }
}

/// <summary>Toggles a named reinforcement layer of an area reinforcement system active/inactive.</summary>
public class SetAreaReinforcementLayersTool : ICortexTool
{
    public string Name => "set_area_reinforcement_layers";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Activate or deactivate a layer of an area reinforcement system. Provide areaReinforcementId, layer (top_major|top_minor|bottom_major|bottom_minor) and active (bool).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var areaId = input["areaReinforcementId"]?.Value<long?>();
        if (areaId == null || areaId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "areaReinforcementId is required");
        var area = doc!.GetElement(ToolHelpers.ToElementId(areaId.Value)) as AreaReinforcement;
        if (area == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No AreaReinforcement with id {areaId}");

        var layerStr = (input["layer"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
        // Verified enum members (R23-R27): TopOrFrontMajor/TopOrFrontMinor/BottomOrBackMajor/BottomOrBackMinor.
        AreaReinforcementLayerType layerType;
        switch (layerStr)
        {
            case "top_major": layerType = AreaReinforcementLayerType.TopOrFrontMajor; break;
            case "top_minor": layerType = AreaReinforcementLayerType.TopOrFrontMinor; break;
            case "bottom_major": layerType = AreaReinforcementLayerType.BottomOrBackMajor; break;
            case "bottom_minor": layerType = AreaReinforcementLayerType.BottomOrBackMinor; break;
            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unsupported layer '{input["layer"]?.Value<string>()}'. Valid: top_major, top_minor, bottom_major, bottom_minor");
        }
        if (input["active"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "active (bool) is required");
        var active = input["active"]!.Value<bool>();

        if (!session.RequestConfirmation("set area reinforcement layer", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Area Reinforcement Layer");
        tx.Start();
        try
        {
            // Verified arg order (R23-R27): SetLayerActive(bool active, AreaReinforcementLayerType layer).
            area.SetLayerActive(active, layerType);
            var isActive = area.IsLayerActive(layerType);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                areaReinforcementId = ToolHelpers.GetElementIdValue(area),
                layer = layerStr,
                active = isActive
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set area reinforcement layer: {ex.Message}");
        }
    }
}

/// <summary>Sets writable options on a path reinforcement system (additional offset; bar orientations).</summary>
public class SetPathReinforcementOptionsTool : ICortexTool
{
    public string Name => "set_path_reinforcement_options";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set options on a path reinforcement system. Provide pathReinforcementId and any of: additionalOffsetMm, primaryBarOrientation (TopOrExterior|BottomOrInterior|NearSide|FarSide), alternatingBarOrientation. Unsupported keys are reported in warnings.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var pathId = input["pathReinforcementId"]?.Value<long?>();
        if (pathId == null || pathId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "pathReinforcementId is required");
        var path = doc!.GetElement(ToolHelpers.ToElementId(pathId.Value)) as PathReinforcement;
        if (path == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No PathReinforcement with id {pathId}");

        var warnings = new List<string>();
        // The plan listed additionalTop/BottomCoverOffset + primaryBarLength; PathReinforcement exposes
        // none of those (they live on AreaReinforcement / are read-only computed values). The real writable
        // surface (verified R23-R27) is AdditionalOffset(double) plus the Primary/Alternating bar orientations.
        if (input["additionalTopCoverOffsetMm"] != null)
            warnings.Add("additionalTopCoverOffsetMm is not settable on a path reinforcement system; ignored");
        if (input["additionalBottomCoverOffsetMm"] != null)
            warnings.Add("additionalBottomCoverOffsetMm is not settable on a path reinforcement system; ignored");
        if (input["primaryBarLengthMm"] != null)
            warnings.Add("primaryBarLengthMm is a computed value and cannot be set directly; ignored");

        ReinforcementBarOrientation? primary = null;
        if (input["primaryBarOrientation"] != null)
        {
            primary = RebarToolHelpers.ParseEnum<ReinforcementBarOrientation>(input["primaryBarOrientation"]!.Value<string>(), "primaryBarOrientation", out var perr);
            if (perr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, perr);
        }
        ReinforcementBarOrientation? alternating = null;
        if (input["alternatingBarOrientation"] != null)
        {
            alternating = RebarToolHelpers.ParseEnum<ReinforcementBarOrientation>(input["alternatingBarOrientation"]!.Value<string>(), "alternatingBarOrientation", out var aerr);
            if (aerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, aerr);
        }

        bool hasOffset = input["additionalOffsetMm"] != null;
        if (!hasOffset && primary == null && alternating == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide at least one of: additionalOffsetMm, primaryBarOrientation, alternatingBarOrientation");

        if (!session.RequestConfirmation("set path reinforcement options", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Path Reinforcement Options");
        tx.Start();
        try
        {
            if (hasOffset)
                path.AdditionalOffset = RebarToolHelpers.FromMm(input["additionalOffsetMm"]!.Value<double>());
            if (primary != null)
            {
                if (path.IsValidPrimaryBarOrientation(primary.Value)) path.PrimaryBarOrientation = primary.Value;
                else warnings.Add($"primaryBarOrientation '{primary}' is not valid for this path; ignored");
            }
            if (alternating != null)
            {
                if (path.IsAlternatingLayerEnabled() && path.IsValidAlternatingBarOrientation(alternating.Value))
                    path.AlternatingBarOrientation = alternating.Value;
                else warnings.Add($"alternatingBarOrientation '{alternating}' is not valid (alternating layer may be disabled); ignored");
            }
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                pathReinforcementId = ToolHelpers.GetElementIdValue(path),
                additionalOffsetMm = RebarToolHelpers.ToMm(path.AdditionalOffset),
                primaryBarOrientation = path.PrimaryBarOrientation.ToString(),
                warnings
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set path reinforcement options: {ex.Message}");
        }
    }
}

/// <summary>
/// Converts an area OR path reinforcement system into standalone rebar elements (DESTRUCTIVE: the
/// system is replaced by individual bars).
/// </summary>
public class ConvertRebarSystemToRebarsTool : ICortexTool
{
    public string Name => "convert_rebar_system_to_rebars";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Convert an area or path reinforcement system into standalone rebars (destructive). Provide systemId (an area or path reinforcement element id). Returns the resulting standalone rebar ids.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var systemId = input["systemId"]?.Value<long?>();
        if (systemId == null || systemId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "systemId is required");
        var elem = doc!.GetElement(ToolHelpers.ToElementId(systemId.Value));
        if (elem == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {systemId}");

        var area = elem as AreaReinforcement;
        var path = elem as PathReinforcement;
        if (area == null && path == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {systemId} ({elem.Category?.Name}) is not an area or path reinforcement system");

        var memberCount = area != null
            ? area.GetRebarInSystemIds().Count
            : path!.GetRebarInSystemIds().Count;

        if (!session.RequestConfirmation("convert reinforcement to rebars", memberCount))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Convert Reinforcement To Rebars");
        tx.Start();
        try
        {
            // Verified across R23-R27: both are STATIC and return IList<ElementId>:
            //   AreaReinforcement.ConvertRebarInSystemToRebars(Document, AreaReinforcement)
            //   PathReinforcement.ConvertRebarInSystemToRebars(Document, PathReinforcement)
            IList<ElementId> resultIds = area != null
                ? AreaReinforcement.ConvertRebarInSystemToRebars(doc, area)
                : PathReinforcement.ConvertRebarInSystemToRebars(doc, path!);
            var rebarIds = resultIds.Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Converted reinforcement {systemId} into {rebarIds.Count} standalone rebar(s)",
                systemId = systemId.Value,
                systemKind = area != null ? "area" : "path",
                rebarCount = rebarIds.Count,
                rebarIds
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to convert reinforcement: {ex.Message}");
        }
    }
}

/// <summary>Removes an area OR path reinforcement system (DESTRUCTIVE).</summary>
public class RemoveRebarSystemTool : ICortexTool
{
    public string Name => "remove_rebar_system";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Remove an area or path reinforcement system (destructive). Provide systemId (an area or path reinforcement element id).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var systemId = input["systemId"]?.Value<long?>();
        if (systemId == null || systemId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "systemId is required");
        var elem = doc!.GetElement(ToolHelpers.ToElementId(systemId.Value));
        if (elem == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {systemId}");

        var area = elem as AreaReinforcement;
        var path = elem as PathReinforcement;
        if (area == null && path == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {systemId} ({elem.Category?.Name}) is not an area or path reinforcement system");

        if (!session.RequestConfirmation("remove reinforcement system", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Remove Reinforcement System");
        tx.Start();
        try
        {
            // Verified across R23-R27: both STATIC, return IList<ElementId> of remaining/affected elements:
            //   AreaReinforcement.RemoveAreaReinforcementSystem(Document, AreaReinforcement)
            //   PathReinforcement.RemovePathReinforcementSystem(Document, PathReinforcement)
            IList<ElementId> remaining = area != null
                ? AreaReinforcement.RemoveAreaReinforcementSystem(doc, area)
                : PathReinforcement.RemovePathReinforcementSystem(doc, path!);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Removed reinforcement system {systemId}",
                systemId = systemId.Value,
                systemKind = area != null ? "area" : "path",
                removed = true,
                remainingElementCount = remaining.Count
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to remove reinforcement system: {ex.Message}");
        }
    }
}

/// <summary>Reads core data of an area reinforcement system (read-only).</summary>
public class GetAreaReinforcementDataTool : ICortexTool
{
    public string Name => "get_area_reinforcement_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read an area reinforcement system: major direction (mm vector), type id/name, host id, member rebar ids, boundary curve ids, member count.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var areaId = input["areaReinforcementId"]?.Value<long?>();
        if (areaId == null || areaId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "areaReinforcementId is required");
        var area = doc!.GetElement(ToolHelpers.ToElementId(areaId.Value)) as AreaReinforcement;
        if (area == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No AreaReinforcement with id {areaId}");

        try
        {
            var memberIds = area.GetRebarInSystemIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var boundaryIds = area.GetBoundaryCurveIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var type = area.AreaReinforcementType;
            return CortexResult<object>.Ok(new
            {
                areaReinforcementId = ToolHelpers.GetElementIdValue(area),
                hostId = ToolHelpers.GetElementIdValue(area.GetHostId()),
                direction = RebarToolHelpers.XyzToDtoMm(area.Direction),
                typeId = ToolHelpers.GetElementIdValue(type),
                typeName = type?.Name,
                memberCount = memberIds.Count,
                memberIds,
                boundaryCurveIds = boundaryIds
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read area reinforcement: {ex.Message}");
        }
    }
}

/// <summary>Reads core data of a path reinforcement system (read-only).</summary>
public class GetPathReinforcementDataTool : ICortexTool
{
    public string Name => "get_path_reinforcement_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a path reinforcement system: type id/name, host id, member rebar ids, curve element ids, additional offset (mm), primary bar orientation.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var pathId = input["pathReinforcementId"]?.Value<long?>();
        if (pathId == null || pathId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "pathReinforcementId is required");
        var path = doc!.GetElement(ToolHelpers.ToElementId(pathId.Value)) as PathReinforcement;
        if (path == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No PathReinforcement with id {pathId}");

        try
        {
            var memberIds = path.GetRebarInSystemIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            // Verified R23-R27: PathReinforcement exposes GetCurveElementIds() (not GetCurveIds/GetBoundaryCurveIds).
            var curveIds = path.GetCurveElementIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var type = path.PathReinforcementType;
            return CortexResult<object>.Ok(new
            {
                pathReinforcementId = ToolHelpers.GetElementIdValue(path),
                hostId = ToolHelpers.GetElementIdValue(path.GetHostId()),
                typeId = ToolHelpers.GetElementIdValue(type),
                typeName = type?.Name,
                additionalOffsetMm = RebarToolHelpers.ToMm(path.AdditionalOffset),
                primaryBarOrientation = path.PrimaryBarOrientation.ToString(),
                alternatingLayerEnabled = path.IsAlternatingLayerEnabled(),
                memberCount = memberIds.Count,
                memberIds,
                curveIds
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read path reinforcement: {ex.Message}");
        }
    }
}
