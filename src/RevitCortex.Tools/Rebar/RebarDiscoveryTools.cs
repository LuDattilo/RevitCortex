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

/// <summary>Lists all RebarBarType elements (id, name, model/nominal diameter in mm).</summary>
[ToolSafety(true, false)]
public class ListRebarBarTypesTool : ICortexTool
{
    public string Name => "list_rebar_bar_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar bar types (id, name, model and nominal diameter in mm).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .Select(t => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(t),
                    ["name"] = t.Name,
                    ["modelDiameterMm"] = RebarToolHelpers.ToMm(t.BarModelDiameter),
                    ["nominalDiameterMm"] = RebarToolHelpers.ToMm(t.BarNominalDiameter)
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, barTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar bar types: {ex.Message}");
        }
    }
}

/// <summary>Lists all RebarHookType elements (id, name, hook angle in degrees).</summary>
[ToolSafety(true, false)]
public class ListRebarHookTypesTool : ICortexTool
{
    public string Name => "list_rebar_hook_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar hook types (id, name, hook angle in degrees).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .Select(h => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(h),
                    ["name"] = h.Name,
                    ["hookAngleDegrees"] = h.HookAngle * 180.0 / Math.PI,
                    ["style"] = h.Style.ToString()
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, hookTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar hook types: {ex.Message}");
        }
    }
}

/// <summary>Lists all RebarShape elements (id, name).</summary>
[ToolSafety(true, false)]
public class ListRebarShapesTool : ICortexTool
{
    public string Name => "list_rebar_shapes";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar shapes (id, name).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .Select(s => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(s),
                    ["name"] = s.Name
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, shapes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar shapes: {ex.Message}");
        }
    }
}

/// <summary>Lists all RebarCoverType elements (id, name, clear cover in mm).</summary>
[ToolSafety(true, false)]
public class ListRebarCoverTypesTool : ICortexTool
{
    public string Name => "list_rebar_cover_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar cover types (id, name, cover distance in mm).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarCoverType)).Cast<RebarCoverType>()
                .Select(c => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(c),
                    ["name"] = c.Name,
                    ["coverDistanceMm"] = RebarToolHelpers.ToMm(c.CoverDistance)
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, coverTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar cover types: {ex.Message}");
        }
    }
}

/// <summary>Lists fabric reinforcement types (FabricSheetType + FabricAreaType).</summary>
[ToolSafety(true, false)]
public class ListRebarFabricTypesTool : ICortexTool
{
    public string Name => "list_rebar_fabric_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List fabric reinforcement types (fabric sheet types and fabric area types) with id and name.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var sheetTypes = new FilteredElementCollector(doc!).OfClass(typeof(FabricSheetType)).Cast<FabricSheetType>()
                .Select(t => new JObject { ["id"] = ToolHelpers.GetElementIdValue(t), ["name"] = t.Name }).ToList();
            var areaTypes = new FilteredElementCollector(doc!).OfClass(typeof(FabricAreaType)).Cast<FabricAreaType>()
                .Select(t => new JObject { ["id"] = ToolHelpers.GetElementIdValue(t), ["name"] = t.Name }).ToList();
            return CortexResult<object>.Ok(new
            {
                fabricSheetTypeCount = sheetTypes.Count,
                fabricSheetTypes = sheetTypes,
                fabricAreaTypeCount = areaTypes.Count,
                fabricAreaTypes = areaTypes
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list fabric types: {ex.Message}");
        }
    }
}

/// <summary>Reports reinforcement hosted in an element: validity, rebar/area/path/fabric counts, common cover.</summary>
[ToolSafety(true, false)]
public class GetRebarHostDataTool : ICortexTool
{
    public string Name => "get_rebar_host_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report reinforcement hosted by an element: whether it is a valid host, and the ids of rebar, area, path and fabric reinforcement it contains, plus common cover.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var hostId = input["hostId"]?.Value<long?>();
        if (hostId == null || hostId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostId is required");
        var host = doc!.GetElement(ToolHelpers.ToElementId(hostId.Value));
        if (host == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {hostId}");

        try
        {
            var isValid = RebarHostData.IsValidHost(host);
            var data = RebarHostData.GetRebarHostData(host);
            if (data == null)
                return CortexResult<object>.Ok(new { hostId, isValidHost = isValid, hasHostData = false });

            List<long> Ids<T>(IEnumerable<T> els) where T : Element =>
                els.Select(e => ToolHelpers.GetElementIdValue(e)).ToList();

            var rebars = Ids(data.GetRebarsInHost());
            var areas = Ids(data.GetAreaReinforcementsInHost());
            var paths = Ids(data.GetPathReinforcementsInHost());
            var fabricSheets = Ids(data.GetFabricSheetsInHost());
            var fabricAreas = Ids(data.GetFabricAreasInHost());

            JObject? commonCover = null;
            try
            {
                var cover = data.GetCommonCoverType();
                if (cover != null)
                    commonCover = new JObject
                    {
                        ["id"] = ToolHelpers.GetElementIdValue(cover),
                        ["name"] = cover.Name,
                        ["coverDistanceMm"] = RebarToolHelpers.ToMm(cover.CoverDistance)
                    };
            }
            catch { /* faces may have mixed cover; leave null */ }

            return CortexResult<object>.Ok(new
            {
                hostId,
                hostCategory = host.Category?.Name,
                isValidHost = isValid,
                hasHostData = true,
                rebarCount = rebars.Count, rebarIds = rebars,
                areaReinforcementCount = areas.Count, areaReinforcementIds = areas,
                pathReinforcementCount = paths.Count, pathReinforcementIds = paths,
                fabricSheetCount = fabricSheets.Count, fabricSheetIds = fabricSheets,
                fabricAreaCount = fabricAreas.Count, fabricAreaIds = fabricAreas,
                commonCover
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read host data: {ex.Message}");
        }
    }
}

/// <summary>Reads a single rebar's core data: type, host, layout rule, quantity, total length, volume.</summary>
[ToolSafety(true, false)]
public class GetRebarElementDataTool : ICortexTool
{
    public string Name => "get_rebar_element_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a single rebar's core data: bar type, host id, shape, layout rule, bar count, total length (mm) and volume.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        try
        {
            var typeId = rebar!.GetTypeId();
            var barType = doc!.GetElement(typeId) as RebarBarType;
            var hostId = rebar.GetHostId();
            var result = new JObject
            {
                ["rebarId"] = ToolHelpers.GetElementIdValue(rebar),
                ["barTypeId"] = ToolHelpers.GetElementIdValue(typeId),
                ["barTypeName"] = barType?.Name,
                ["barDiameterMm"] = barType != null ? RebarToolHelpers.ToMm(barType.BarNominalDiameter) : (double?)null,
                ["hostId"] = ToolHelpers.GetElementIdValue(hostId),
                ["isShapeDriven"] = rebar.IsRebarShapeDriven(),
                ["isFreeForm"] = rebar.IsRebarFreeForm(),
                ["layoutRule"] = rebar.LayoutRule.ToString(),
                ["numberOfBarPositions"] = rebar.NumberOfBarPositions,
                ["quantity"] = rebar.Quantity,
                ["totalLengthMm"] = RebarToolHelpers.ToMm(rebar.TotalLength),
                ["volumeCuMm"] = rebar.Volume * Math.Pow(RebarToolHelpers.MmPerFoot, 3),
                ["scheduleMark"] = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK)?.AsString()
            };
            if (rebar.IsRebarShapeDriven())
            {
                var shapeId = rebar.GetShapeId();
                result["shapeId"] = ToolHelpers.GetElementIdValue(shapeId);
                result["shapeName"] = (doc.GetElement(shapeId) as RebarShape)?.Name;
            }
            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar data: {ex.Message}");
        }
    }
}

/// <summary>Returns centerline curves (mm) for one bar position of a rebar. Opt-in detail.</summary>
[ToolSafety(true, false)]
public class GetRebarGeometryTool : ICortexTool
{
    public string Name => "get_rebar_geometry";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Return the centerline curves (mm) of a rebar at a given bar position index (default 0). Use suppressHooks/suppressBendRadius to simplify.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var barIndex = input["barPositionIndex"]?.Value<int?>() ?? 0;
        var suppressHooks = input["suppressHooks"]?.Value<bool?>() ?? false;
        var suppressBend = input["suppressBendRadius"]?.Value<bool?>() ?? false;
        try
        {
            // Positional args: the 2nd parameter was renamed suppressHooks -> suppressHooksAndCranks
            // in Revit 2026, so named arguments would not compile across all targets. Position and
            // types are identical across 2023-2027.
            var curves = rebar!.GetCenterlineCurves(
                true,                                       // adjustForSelfIntersection
                suppressHooks,                              // suppressHooks / suppressHooksAndCranks (2026+)
                suppressBend,                               // suppressBendRadius
                MultiplanarOption.IncludeOnlyPlanarCurves,  // multiplanarOption
                barIndex);                                  // barPositionIndex
            var dtos = curves.Select(RebarToolHelpers.CurveToDtoMm).ToList();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                barPositionIndex = barIndex,
                numberOfBarPositions = rebar.NumberOfBarPositions,
                curveCount = dtos.Count,
                curves = dtos
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar geometry: {ex.Message}");
        }
    }
}

/// <summary>Lists the constrained handles on a rebar (read-only summary).</summary>
[ToolSafety(true, false)]
public class GetRebarConstraintsTool : ICortexTool
{
    public string Name => "get_rebar_constraints";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List the constrained handles of a rebar and whether its constraints can be edited.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        try
        {
            var mgr = rebar!.GetRebarConstraintsManager();
            if (mgr == null)
                return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), constraintsAvailable = false });
            var handles = mgr.GetAllHandles();
            var handleDtos = handles.Select(h => new JObject
            {
                ["handleType"] = h.GetHandleType().ToString()
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                constraintsAvailable = true,
                canBeEdited = rebar.ConstraintsCanBeEdited(),
                handleCount = handleDtos.Count,
                handles = handleDtos
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar constraints: {ex.Message}");
        }
    }
}

/// <summary>Reads document-level reinforcement settings.</summary>
[ToolSafety(true, false)]
public class GetReinforcementSettingsTool : ICortexTool
{
    public string Name => "get_reinforcement_settings";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read document-level reinforcement settings (host structural rebar, presentation modes, shape-defines-hooks/terminations).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var s = ReinforcementSettings.GetReinforcementSettings(doc!);
            var result = new JObject
            {
                ["hostStructuralRebar"] = s.HostStructuralRebar
            };
            // Property names differ across versions; read defensively.
            try { result["rebarShapeDefinesHooks"] = s.RebarShapeDefinesHooks; } catch { }
            try { result["rebarShapeDefinesEndTreatments"] = s.RebarShapeDefinesEndTreatments; } catch { }
            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read reinforcement settings: {ex.Message}");
        }
    }
}

/// <summary>Lists rebar splice types (Revit 2025+ only).</summary>
[ToolSafety(true, false)]
public class ListRebarSpliceTypesTool : ICortexTool
{
    public string Name => "list_rebar_splice_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List rebar splice types (Revit 2025+). Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
#if REVIT2025_OR_GREATER
        try
        {
            // There is no public RebarSpliceType element class in the API. Revit models
            // mechanical splices as rebar couplers, whose "types" are FamilySymbols in the
            // Coupler category (OST_Coupler). Collect those as the splice/coupler types.
            var items = new FilteredElementCollector(doc!)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Coupler)
                .Cast<FamilySymbol>()
                .Select(t => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(t),
                    ["name"] = t.Name,
                    ["familyName"] = t.Family?.Name
                }).ToList();
            return CortexResult<object>.Ok(new
            {
                count = items.Count,
                spliceTypes = items,
                note = "Splice/coupler types are FamilySymbols in the Coupler category (OST_Coupler); the API has no dedicated RebarSpliceType element class."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list splice types: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar splices", 2025);
#endif
    }
}

/// <summary>Reports which version-gated reinforcement APIs the running Revit supports.</summary>
[ToolSafety(true, false)]
public class GetRebarApiCapabilitiesTool : ICortexTool
{
    public string Name => "get_rebar_api_capabilities";
    public string Category => "Rebar";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report which version-gated reinforcement features the running Revit supports (terminations/crank, splices, bending details), plus server-only APIs that are not runtime-scriptable.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        int year =
#if REVIT2027_OR_GREATER
            2027;
#elif REVIT2026_OR_GREATER
            2026;
#elif REVIT2025_OR_GREATER
            2025;
#elif REVIT2024_OR_GREATER
            2024;
#else
            2023;
#endif
        return CortexResult<object>.Ok(new
        {
            revitYear = year,
            supportsBendingDetails = year >= 2024,
            supportsAlignedFreeForm = year >= 2024,
            supportsSplices = year >= 2025,
            supportsSurfaceConstraints = year >= 2025,
            supportsVaryingLengthBars = year >= 2025,
            supportsTerminationsApi = year >= 2026,   // BarTerminationsData / RebarTerminationOrientation
            supportsCrankApi = year >= 2026,
            supports3dPathDistribution = year >= 2027,
            // Server-extension APIs (IRebarUpdateServer / IRebarSpliceServer) are add-in lifecycle
            // infrastructure and are intentionally NOT scriptable from MCP input.
            serverExtensionApisScriptable = false
        });
    }
}
