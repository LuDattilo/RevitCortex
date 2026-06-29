using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Steel;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

// =====================================================================================
// Module 1 — Discovery & inventory (15 read-only tools).
//
// API VERIFICATION (reflected against Nice3point ref RevitAPI.dll 2023.1.90 / 2024.3.40 /
// 2025.4.50 / 2026.4.10 / 2027.0.20 via MetadataLoadContext — identical across versions
// except where noted). The original plan's instance-method assumptions were WRONG for
// SteelElementProperties and for several connection-handler members; the real surface is:
//
//   Autodesk.Revit.DB.Steel.SteelElementProperties  (base APIObject) — ONLY:
//     static SteelElementProperties GetSteelElementProperties(Element)
//     static Guid GetFabricationUniqueID(Document, Reference)
//     static Reference GetReference(Document, Guid)
//     static IList<ElementId> AddFabricationInformationForRevitElements(Document, IList<ElementId>)
//     instance: Guid UniqueID { get; set; }, bool IsValidObject { get; }
//   => There is NO instance GetFabricationUniqueID(), NO GetAllExternalIds(),
//      NO GetAllRevitMaterialsIds(), NO GetExternalId(), NO GetCurrWarnings(),
//      NO GetElemsWithWarnings(), NO CountOfAsyncWarnings(). The whole external-id /
//      material-link / steel-warning instance API in the plan does not exist in the SDK.
//      The Steel namespace contains ONLY SteelElementProperties.
//
//   StructuralConnectionHandler (base Element):
//     IList<ElementId> GetConnectedElementIds(); XYZ GetOrigin();
//     bool IsCustom(); bool IsDetailed();
//     IList<ConnectionInputPoint> GetInputPoints(); IList<Reference> GetInputReferences();
//     properties: ElementId ApprovalTypeId; StructuralConnectionCodeCheckingStatus CodeCheckingStatus;
//                 bool OverrideTypeParams; int SingleElementEndIndex.
//     (GetTypeId() inherited from Element.) => There is NO GetFailed() and NO "disconnected" member.
//
//   StructuralConnectionType.GetAllStructuralConnectionTypeIds(Document, out ICollection<ElementId>)
//     — OUT parameter, not a return value. Also GetFamilySymbolId(); ApplyTo.
//   StructuralConnectionApprovalType.GetAllStructuralConnectionApprovalTypes(Document, out ICollection<ElementId>)
//     — OUT parameter (NOT collected via OfClass).
//   StructuralConnectionHandlerType (base ElementType): ConnectionGuid; IsCustom/IsDetailed/IsGeneric().
//   StructuralConnectionSettings.GetStructuralConnectionSettings(Document); bool IncludeWarningControls.
//   StructuralConnectionsProviderRegistry: NO public query/ctor — only Dispose()/IsValidObject.
//     => provider availability is NOT queryable; AnyConnectionProviderInstalled() is best-effort false.
//   SolidSolidCutUtils.GetCuttingSolids(Element)/GetSolidsBeingCut(Element) -> ICollection<ElementId>.
//   InstanceVoidCutUtils.GetCuttingVoidInstances(Element)/GetElementsBeingCut(Element) -> ICollection<ElementId>.
// =====================================================================================

/// <summary>Reports which version-gated + provider-dependent steel features the running Revit supports.</summary>
[ToolSafety(true, false)]
public class GetStructuralSteelApiCapabilitiesTool : ICortexTool
{
    public string Name => "get_structural_steel_api_capabilities";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report which structural steel features the running Revit supports: SteelElementProperties, structural connections, cut utils, custom-connection mutation API (replaced in R27), and whether any structural connection provider is detectable.";

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
        // Provider presence is a runtime fact. The public registry exposes no query method, so this is
        // best-effort and currently always false (see AnyConnectionProviderInstalled).
        bool providerInstalled = false;
        try { providerInstalled = StructuralSteelToolHelpers.AnyConnectionProviderInstalled(); }
        catch { /* registry unqueryable */ }

        return CortexResult<object>.Ok(new
        {
            revitYear = year,
            supportsSteelElementProperties = true,   // confirmed R23-R27 (Autodesk.Revit.DB.Steel.SteelElementProperties)
            supportsStructuralConnections = true,
            supportsSolidSolidCutUtils = true,
            supportsInstanceVoidCutUtils = true,
            // The legacy AddElementsToCustomConnection / RemoveMainSubelementsFromCustomConnection static
            // mutators exist R23-R26 and are REMOVED in R27 (replaced by UpdateCustomConnectionType).
            // NOTE (back-compat): supportsCustomConnectionMutation is kept but is misleading on its own
            // (true reads as "can mutate"); supportsCustomConnectionMutationFromJson is the honest field.
            supportsCustomConnectionMutation = year <= 2026,
            // NOTE (back-compat): connectionProviderInstalled is kept but the public registry exposes no
            // query method — false means "not queryable", NOT "provider absent". Use the explicit fields below.
            connectionProviderInstalled = providerInstalled,
            connectionProviderDetection = "not_queryable_public_api",
            connectionProviderState = "unknown",
            customConnectionMutationApiMembersExist = (year <= 2026),   // legacy add/remove statics
            supportsCustomConnectionMutationFromJson = false,           // never, on any version
            customConnectionMutationReason = (year >= 2027
                ? "legacy add/remove APIs removed in R27; UpdateCustomConnectionType still requires interactive Reference objects"
                : "requires interactive Reference/Subelement objects not expressible as JSON"),
            supportsSetSteelFabricationUniqueId = "best_effort_non_public_setter",
            note = "Steel fabrication external-id, material-link and per-element warning APIs assumed in the design are NOT present in the public Revit SDK (Autodesk.Revit.DB.Steel exposes only SteelElementProperties). Structural connection provider availability is NOT queryable via the public registry (connectionProviderState is unknown, not 'absent'). Custom-connection mutation is not expressible from JSON on any version (supportsCustomConnectionMutationFromJson=false)."
        });
    }
}

/// <summary>Lists structural connection handlers (id, type id/name, connected element count). Capped.</summary>
[ToolSafety(true, false)]
public class ListSteelConnectionHandlersTool : ICortexTool
{
    public string Name => "list_steel_connection_handlers";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List structural connection handlers: id, type id/name, connected element count, custom/detailed flags. Use maxResults (default 100) and summaryOnly for counts-first.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var max = input["maxResults"]?.Value<int?>() ?? 100;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            var all = new FilteredElementCollector(doc!).OfClass(typeof(StructuralConnectionHandler))
                .Cast<StructuralConnectionHandler>().ToList();
            if (summaryOnly)
                return CortexResult<object>.Ok(new { count = all.Count, summaryOnly = true });

            var items = all.Take(max).Select(h =>
            {
                int connected = 0;
                try { connected = h.GetConnectedElementIds()?.Count ?? 0; } catch { }
                var typeId = h.GetTypeId();
                string? typeName = null;
                try { typeName = doc!.GetElement(typeId)?.Name; } catch { }
                return new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(h),
                    ["typeId"] = ToolHelpers.GetElementIdValue(typeId),
                    ["typeName"] = typeName,
                    ["connectedElementCount"] = connected
                };
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                count = all.Count,
                returnedCount = items.Count,
                truncated = all.Count > max,
                handlers = items
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list connection handlers: {ex.Message}");
        }
    }
}

/// <summary>Lists structural connection types (id, name, family symbol id, applyTo). Capped.</summary>
[ToolSafety(true, false)]
public class ListSteelConnectionTypesTool : ICortexTool
{
    public string Name => "list_steel_connection_types";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List StructuralConnectionType definitions: id, name, family symbol id, applyTo. Use maxResults (default 100) and summaryOnly for counts-first.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var max = input["maxResults"]?.Value<int?>() ?? 100;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            // Reflected: static void GetAllStructuralConnectionTypeIds(Document, out ICollection<ElementId>)
            ICollection<ElementId> typeIds;
            StructuralConnectionType.GetAllStructuralConnectionTypeIds(doc!, out typeIds);
            var allIds = typeIds != null ? typeIds.ToList() : new List<ElementId>();
            if (summaryOnly)
                return CortexResult<object>.Ok(new { count = allIds.Count, summaryOnly = true });

            var items = allIds.Take(max).Select(id =>
            {
                var ct = doc!.GetElement(id) as StructuralConnectionType;
                long familySymbolId = 0;
                string? applyTo = null;
                if (ct != null)
                {
                    try { familySymbolId = ToolHelpers.GetElementIdValue(ct.GetFamilySymbolId()); } catch { }
                    try { applyTo = ct.ApplyTo.ToString(); } catch { }
                }
                return new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(id),
                    ["name"] = ct?.Name,
                    ["familySymbolId"] = familySymbolId,
                    ["applyTo"] = applyTo
                };
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                count = allIds.Count,
                returnedCount = items.Count,
                truncated = allIds.Count > max,
                connectionTypes = items
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list connection types: {ex.Message}");
        }
    }
}

/// <summary>Lists structural connection handler types (id, name, connection GUID, kind flags). Capped.</summary>
[ToolSafety(true, false)]
public class ListSteelConnectionHandlerTypesTool : ICortexTool
{
    public string Name => "list_steel_connection_handler_types";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List StructuralConnectionHandlerType definitions: id, name, connection GUID, generic/custom/detailed flags. Use maxResults (default 100) and summaryOnly for counts-first.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var max = input["maxResults"]?.Value<int?>() ?? 100;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            var all = new FilteredElementCollector(doc!).OfClass(typeof(StructuralConnectionHandlerType))
                .Cast<StructuralConnectionHandlerType>().ToList();
            if (summaryOnly)
                return CortexResult<object>.Ok(new { count = all.Count, summaryOnly = true });

            var items = all.Take(max).Select(t =>
            {
                string? guid = null; bool isGeneric = false, isCustom = false, isDetailed = false;
                try { guid = t.ConnectionGuid.ToString(); } catch { }
                try { isGeneric = t.IsGeneric(); } catch { }
                try { isCustom = t.IsCustom(); } catch { }
                try { isDetailed = t.IsDetailed(); } catch { }
                return new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(t),
                    ["name"] = t.Name,
                    ["connectionGuid"] = guid,
                    ["isGeneric"] = isGeneric,
                    ["isCustom"] = isCustom,
                    ["isDetailed"] = isDetailed
                };
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                count = all.Count,
                returnedCount = items.Count,
                truncated = all.Count > max,
                handlerTypes = items
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list connection handler types: {ex.Message}");
        }
    }
}

/// <summary>Lists structural connection approval types (id, name). Capped.</summary>
[ToolSafety(true, false)]
public class ListSteelApprovalTypesTool : ICortexTool
{
    public string Name => "list_steel_approval_types";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List StructuralConnectionApprovalType definitions: id, name. Use maxResults (default 100) and summaryOnly for counts-first.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var max = input["maxResults"]?.Value<int?>() ?? 100;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            // Reflected: static void GetAllStructuralConnectionApprovalTypes(Document, out ICollection<ElementId>)
            ICollection<ElementId> approvalIds;
            StructuralConnectionApprovalType.GetAllStructuralConnectionApprovalTypes(doc!, out approvalIds);
            var allIds = approvalIds != null ? approvalIds.ToList() : new List<ElementId>();
            if (summaryOnly)
                return CortexResult<object>.Ok(new { count = allIds.Count, summaryOnly = true });

            var items = allIds.Take(max).Select(id => new JObject
            {
                ["id"] = ToolHelpers.GetElementIdValue(id),
                ["name"] = doc!.GetElement(id)?.Name
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                count = allIds.Count,
                returnedCount = items.Count,
                truncated = allIds.Count > max,
                approvalTypes = items
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list approval types: {ex.Message}");
        }
    }
}

/// <summary>Lists installed structural connection providers (best-effort; the public registry is not queryable).</summary>
[ToolSafety(true, false)]
public class ListSteelConnectionProvidersTool : ICortexTool
{
    public string Name => "list_steel_connection_providers";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List structural connection providers. The public Revit API exposes no queryable provider registry, so this returns count 0 with an explanatory note rather than guessing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        if (docResult.Item2 != null) return docResult.Item2;
        // StructuralConnectionsProviderRegistry exposes no public query method or constructor (reflected
        // R23-R27): only Dispose()/IsValidObject. Provider enumeration is therefore not possible.
        return CortexResult<object>.Ok(new
        {
            count = 0,
            providers = new object[0],
            note = "StructuralConnectionsProviderRegistry has no public query API; installed connection providers cannot be enumerated via the public SDK. Use get_structural_steel_api_capabilities for the best-effort availability flag."
        });
    }
}

/// <summary>Reads one structural connection handler's readable state (type, connected ids, origin, status flags).</summary>
[ToolSafety(true, false)]
public class GetSteelConnectionDataTool : ICortexTool
{
    public string Name => "get_steel_connection_data";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a structural connection handler: type id/name, connected element ids, origin (mm), custom/detailed flags, approval type id, code-checking status, override-type-params. Provider-dependent fields are read defensively.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var handlerResult = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        var handler = handlerResult.Item1;
        if (handlerResult.Item2 != null) return handlerResult.Item2;
        var warnings = new List<string>();
        try
        {
            var typeId = handler!.GetTypeId();
            string? typeName = null;
            try { typeName = doc!.GetElement(typeId)?.Name; } catch { }

            List<long> connected = new List<long>();
            try { connected = handler.GetConnectedElementIds()?.Select(i => ToolHelpers.GetElementIdValue(i)).ToList() ?? new List<long>(); }
            catch (Exception ex) { warnings.Add($"GetConnectedElementIds unavailable: {ex.Message}"); }

            JObject? origin = null;
            try { origin = StructuralSteelToolHelpers.XyzToDtoMm(handler.GetOrigin()); }
            catch (Exception ex) { warnings.Add($"GetOrigin unavailable: {ex.Message}"); }

            bool? isCustom = null, isDetailed = null, overrideTypeParams = null;
            long approvalTypeId = 0; string? codeCheckingStatus = null; int? singleElementEndIndex = null;
            try { isCustom = handler.IsCustom(); } catch { }
            try { isDetailed = handler.IsDetailed(); } catch { }
            try { overrideTypeParams = handler.OverrideTypeParams; } catch { }
            try { approvalTypeId = ToolHelpers.GetElementIdValue(handler.ApprovalTypeId); } catch { }
            try { codeCheckingStatus = handler.CodeCheckingStatus.ToString(); } catch { }
            try { singleElementEndIndex = handler.SingleElementEndIndex; } catch { }

            var payload = new JObject
            {
                ["connectionId"] = ToolHelpers.GetElementIdValue(handler),
                ["typeId"] = ToolHelpers.GetElementIdValue(typeId),
                ["typeName"] = typeName,
                ["connectedElementIds"] = new JArray(connected.Cast<object>().ToArray()),
                ["connectedElementCount"] = connected.Count,
                ["origin"] = origin,
                ["isCustom"] = isCustom,
                ["isDetailed"] = isDetailed,
                ["approvalTypeId"] = approvalTypeId,
                ["codeCheckingStatus"] = codeCheckingStatus,
                ["overrideTypeParams"] = overrideTypeParams,
                ["singleElementEndIndex"] = singleElementEndIndex
            };
            if (warnings.Count > 0) payload["warnings"] = new JArray(warnings.Cast<object>().ToArray());
            // Note: the Revit API exposes no "failed"/"disconnected" state on StructuralConnectionHandler.
            payload["note"] = "Revit's StructuralConnectionHandler exposes no failed/disconnected state; only the fields above are readable.";
            return CortexResult<object>.Ok(payload);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read connection data: {ex.Message}");
        }
    }
}

/// <summary>Reads one connection type's readable data (kind, family symbol, applyTo / handler-type flags).</summary>
[ToolSafety(true, false)]
public class GetSteelConnectionTypeDataTool : ICortexTool
{
    public string Name => "get_steel_connection_type_data";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a structural connection type by id. For StructuralConnectionType: family symbol id + applyTo. For StructuralConnectionHandlerType: connection GUID + generic/custom/detailed flags. Input points are per-handler-instance, not per-type.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var elemResult = StructuralSteelToolHelpers.RequireElement(doc!, input["connectionTypeId"]?.Value<long?>());
        var elem = elemResult.Item1;
        if (elemResult.Item2 != null) return elemResult.Item2;
        try
        {
            var payload = new JObject
            {
                ["connectionTypeId"] = ToolHelpers.GetElementIdValue(elem!),
                ["name"] = elem!.Name
            };

            var connType = elem as StructuralConnectionType;
            if (connType != null)
            {
                payload["kind"] = "StructuralConnectionType";
                try { payload["familySymbolId"] = ToolHelpers.GetElementIdValue(connType.GetFamilySymbolId()); } catch { }
                try { payload["applyTo"] = connType.ApplyTo.ToString(); } catch { }
            }

            var handlerType = elem as StructuralConnectionHandlerType;
            if (handlerType != null)
            {
                payload["kind"] = "StructuralConnectionHandlerType";
                try { payload["connectionGuid"] = handlerType.ConnectionGuid.ToString(); } catch { }
                try { payload["isGeneric"] = handlerType.IsGeneric(); } catch { }
                try { payload["isCustom"] = handlerType.IsCustom(); } catch { }
                try { payload["isDetailed"] = handlerType.IsDetailed(); } catch { }
            }

            if (connType == null && handlerType == null)
                payload["note"] = "Element is neither a StructuralConnectionType nor a StructuralConnectionHandlerType; only id/name returned.";
            else
                payload["inputPointsNote"] = "Connection input points are exposed on connection-handler instances (GetInputPoints), not on the type. Use get_steel_connection_data on a handler that uses this type.";
            return CortexResult<object>.Ok(payload);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read connection type data: {ex.Message}");
        }
    }
}

/// <summary>Reads the document-wide structural connection settings.</summary>
[ToolSafety(true, false)]
public class GetSteelConnectionSettingsTool : ICortexTool
{
    public string Name => "get_steel_connection_settings";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the document's StructuralConnectionSettings (currently exposes the IncludeWarningControls flag).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        try
        {
            // Reflected: static StructuralConnectionSettings GetStructuralConnectionSettings(Document)
            var settings = StructuralConnectionSettings.GetStructuralConnectionSettings(doc!);
            if (settings == null)
                return CortexResult<object>.Ok(new { hasSettings = false, note = "No StructuralConnectionSettings element in this document." });
            bool? includeWarningControls = null;
            try { includeWarningControls = settings.IncludeWarningControls; } catch { }
            return CortexResult<object>.Ok(new
            {
                hasSettings = true,
                settingsId = ToolHelpers.GetElementIdValue(settings),
                includeWarningControls = includeWarningControls
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read connection settings: {ex.Message}");
        }
    }
}

/// <summary>Reads SteelElementProperties summary for an element (presence + fabrication unique id).</summary>
[ToolSafety(true, false)]
public class GetSteelElementPropertiesTool : ICortexTool
{
    public string Name => "get_steel_element_properties";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read steel fabrication properties of an element: whether it has SteelElementProperties and its fabrication unique id (GUID). External-id and material-link enumeration are not exposed by the Revit SDK. Use summaryOnly for flags only.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var elemResult = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        var elem = elemResult.Item1;
        if (elemResult.Item2 != null) return elemResult.Item2;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            // Reflected: static SteelElementProperties GetSteelElementProperties(Element) -> instance or null.
            var props = SteelElementProperties.GetSteelElementProperties(elem!);
            if (props == null)
                return CortexResult<object>.Ok(new { elementId = ToolHelpers.GetElementIdValue(elem!), hasSteelProperties = false });

            // The only readable instance member is the UniqueID GUID (the fabrication unique id).
            string? fabId = null;
            try { fabId = props.UniqueID.ToString(); } catch { }
            if (summaryOnly)
                return CortexResult<object>.Ok(new
                {
                    elementId = ToolHelpers.GetElementIdValue(elem!),
                    hasSteelProperties = true
                });
            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                hasSteelProperties = true,
                fabricationUniqueId = fabId,
                note = "SteelElementProperties exposes only the fabrication unique id (UniqueID). External ids and linked material ids are not available through the public Revit SDK."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read steel element properties: {ex.Message}");
        }
    }
}

/// <summary>Reports the steel fabrication external-id mapping for an element (not exposed by the SDK).</summary>
[ToolSafety(true, false)]
public class GetSteelExternalIdMapTool : ICortexTool
{
    public string Name => "get_steel_external_id_map";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report the steel fabrication external-id map for an element. The Revit SDK does not expose per-element external-id enumeration, so this returns the fabrication unique id (if any) plus count 0 with a note.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var elemResult = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        var elem = elemResult.Item1;
        if (elemResult.Item2 != null) return elemResult.Item2;
        try
        {
            var props = SteelElementProperties.GetSteelElementProperties(elem!);
            string? fabId = null;
            if (props != null) { try { fabId = props.UniqueID.ToString(); } catch { } }
            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                hasSteelProperties = props != null,
                fabricationUniqueId = fabId,
                count = 0,
                externalIds = new object[0],
                note = "SteelElementProperties exposes no external-id enumeration in the public Revit SDK (no GetAllExternalIds/GetExternalId); only the fabrication unique id is available."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read steel external id map: {ex.Message}");
        }
    }
}

/// <summary>Reports the steel material links for an element (not exposed by the SDK).</summary>
[ToolSafety(true, false)]
public class GetSteelMaterialLinksTool : ICortexTool
{
    public string Name => "get_steel_material_links";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report steel fabrication material links for an element. The Revit SDK does not expose linked-material enumeration on SteelElementProperties, so this returns count 0 with a note.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var elemResult = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        var elem = elemResult.Item1;
        if (elemResult.Item2 != null) return elemResult.Item2;
        try
        {
            var props = SteelElementProperties.GetSteelElementProperties(elem!);
            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                hasSteelProperties = props != null,
                count = 0,
                materialLinkIds = new object[0],
                note = "SteelElementProperties exposes no linked-material enumeration in the public Revit SDK (no GetAllRevitMaterialsIds). Read the element's standard material parameters instead."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read steel material links: {ex.Message}");
        }
    }
}

/// <summary>Reports steel fabrication warnings (not exposed by the SDK; falls back to a note).</summary>
[ToolSafety(true, false)]
public class GetSteelElementWarningsTool : ICortexTool
{
    public string Name => "get_steel_element_warnings";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report steel fabrication warnings. The Revit SDK exposes no steel-specific warning API (no GetCurrWarnings/CountOfAsyncWarnings), so this returns count 0 with a note. Use the general get_warnings tool for document failures. summaryOnly returns counts only.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        // Optional elementId — validate only if supplied (the steel warning API does not exist either way).
        var idToken = input["elementId"]?.Value<long?>();
        long? elementId = null;
        if (idToken != null && idToken > 0)
        {
            var elemResult = StructuralSteelToolHelpers.RequireElement(doc!, idToken);
            if (elemResult.Item2 != null) return elemResult.Item2;
            elementId = ToolHelpers.GetElementIdValue(elemResult.Item1!);
        }
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        if (summaryOnly)
            return CortexResult<object>.Ok(new
            {
                elementId = elementId,
                currentWarningCount = 0,
                queuedAsyncWarningCount = 0,
                summaryOnly = true,
                note = "No steel-specific warning API in the public Revit SDK."
            });
        return CortexResult<object>.Ok(new
        {
            elementId = elementId,
            currentWarningCount = 0,
            queuedAsyncWarningCount = 0,
            warnings = new object[0],
            note = "SteelElementProperties exposes no warning API in the public Revit SDK (no GetCurrWarnings/GetElemsWithWarnings/CountOfAsyncWarnings). Use the general get_warnings tool for document-level failures."
        });
    }
}

/// <summary>Reads solid-solid and instance-void cut relationships for an element.</summary>
[ToolSafety(true, false)]
public class GetSteelCutDataTool : ICortexTool
{
    public string Name => "get_steel_cut_data";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read cut relationships for an element: solid-solid cuts (cutting solids + solids being cut) and instance-void cuts (cutting void instances + elements being cut).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var elemResult = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        var elem = elemResult.Item1;
        if (elemResult.Item2 != null) return elemResult.Item2;
        try
        {
            // Reflected: SolidSolidCutUtils.GetCuttingSolids/GetSolidsBeingCut(Element) -> ICollection<ElementId>
            //            InstanceVoidCutUtils.GetCuttingVoidInstances/GetElementsBeingCut(Element) -> ICollection<ElementId>
            var cuttingSolids = SafeIds(() => SolidSolidCutUtils.GetCuttingSolids(elem!));
            var solidsBeingCut = SafeIds(() => SolidSolidCutUtils.GetSolidsBeingCut(elem!));
            var cuttingVoids = SafeIds(() => InstanceVoidCutUtils.GetCuttingVoidInstances(elem!));
            var elementsBeingCut = SafeIds(() => InstanceVoidCutUtils.GetElementsBeingCut(elem!));

            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                solidCut = new
                {
                    cuttingSolidCount = cuttingSolids.Count,
                    cuttingSolidIds = cuttingSolids,
                    solidsBeingCutCount = solidsBeingCut.Count,
                    solidsBeingCutIds = solidsBeingCut
                },
                instanceVoidCut = new
                {
                    cuttingVoidCount = cuttingVoids.Count,
                    cuttingVoidInstanceIds = cuttingVoids,
                    elementsBeingCutCount = elementsBeingCut.Count,
                    elementsBeingCutIds = elementsBeingCut
                }
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read cut data: {ex.Message}");
        }
    }

    private static List<long> SafeIds(Func<ICollection<ElementId>> getter)
    {
        try
        {
            var ids = getter();
            return ids != null ? ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList() : new List<long>();
        }
        catch { return new List<long>(); }
    }
}

/// <summary>Document-wide structural steel summary: counts of handlers, types, steel-property elements, cuts.</summary>
[ToolSafety(true, false)]
public class AnalyzeStructuralSteelModelTool : ICortexTool
{
    public string Name => "analyze_structural_steel_model";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Document-wide structural steel summary: counts of connection handlers, connection types, connection handler types, approval types, and structural framing/column elements carrying SteelElementProperties. summaryOnly returns counts only; otherwise capped sample arrays via maxResults.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;
        var max = input["maxResults"]?.Value<int?>() ?? 100;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            var handlers = new FilteredElementCollector(doc!).OfClass(typeof(StructuralConnectionHandler))
                .Cast<StructuralConnectionHandler>().ToList();

            ICollection<ElementId> connTypeIds;
            StructuralConnectionType.GetAllStructuralConnectionTypeIds(doc!, out connTypeIds);
            int connectionTypeCount = connTypeIds != null ? connTypeIds.Count : 0;

            var handlerTypes = new FilteredElementCollector(doc!).OfClass(typeof(StructuralConnectionHandlerType))
                .Cast<StructuralConnectionHandlerType>().ToList();

            ICollection<ElementId> approvalIds;
            StructuralConnectionApprovalType.GetAllStructuralConnectionApprovalTypes(doc!, out approvalIds);
            int approvalTypeCount = approvalIds != null ? approvalIds.Count : 0;

            // Elements carrying SteelElementProperties — scan steel-bearing categories (framing + columns).
            var steelCandidates = new FilteredElementCollector(doc!)
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_StructuralColumns
                }))
                .WhereElementIsNotElementType()
                .ToList();
            var elementsWithSteelProps = new List<Element>();
            foreach (var e in steelCandidates)
            {
                try { if (SteelElementProperties.GetSteelElementProperties(e) != null) elementsWithSteelProps.Add(e); }
                catch { }
            }

            if (summaryOnly)
                return CortexResult<object>.Ok(new
                {
                    summaryOnly = true,
                    connectionHandlerCount = handlers.Count,
                    connectionTypeCount,
                    connectionHandlerTypeCount = handlerTypes.Count,
                    approvalTypeCount,
                    elementsWithSteelPropertiesCount = elementsWithSteelProps.Count,
                    steelCandidatesScanned = steelCandidates.Count
                });

            return CortexResult<object>.Ok(new
            {
                connectionHandlerCount = handlers.Count,
                connectionTypeCount,
                connectionHandlerTypeCount = handlerTypes.Count,
                approvalTypeCount,
                elementsWithSteelPropertiesCount = elementsWithSteelProps.Count,
                steelCandidatesScanned = steelCandidates.Count,
                sampleConnectionHandlerIds = handlers.Take(max).Select(h => ToolHelpers.GetElementIdValue(h)).ToList(),
                sampleElementsWithSteelProperties = elementsWithSteelProps.Take(max).Select(e => ToolHelpers.GetElementIdValue(e)).ToList(),
                note = "Cut relationships are per-element; use get_steel_cut_data for a specific element. SteelElementProperties scan is limited to structural framing + columns."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to analyze structural steel model: {ex.Message}");
        }
    }
}
