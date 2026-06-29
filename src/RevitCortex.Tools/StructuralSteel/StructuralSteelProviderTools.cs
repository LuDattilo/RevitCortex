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

namespace RevitCortex.Tools.StructuralSteel;

// =====================================================================================
// Module 6 — Provider & extension reporting (3 read-only tools).
//
// API VERIFIED 2026-05-30 (reflection R25). The structural-connection PROVIDER
// infrastructure is NOT publicly queryable, so these tools honestly report
// availability rather than fabricating data — they never throw:
//
//   StructuralConnectionsProviderRegistry  — only Dispose()/IsValidObject; NO public
//     query method, NO public constructor. Providers cannot be enumerated from the API.
//   StructuralConnectionsProviderData       — only Dispose()/IsValidObject; an opaque
//     buffer a provider fills via callback, not readable by a caller.
//   IStructuralConnectionsProvider          — a provider-IMPLEMENTED interface
//     (GetAvailableConnectionTypes/GetTypeInfo/...), not a queryable registry list.
//   ConnectionValidationInfo                — has a public .ctor() + GetWarning(int) +
//     ManyWarnings() + IsValidWarningIndex(int), but NO public method produces a
//     POPULATED instance from a placed handler (Revit fills it internally during
//     provider-driven validation). So validation reporting falls back to the handler's
//     own CodeCheckingStatus property.
//   ConnectionValidationWarning             — Reason (ConnectionWarning enum:
//     Unknown/Alignment/Size/Shape/Connectivity), Resolution (ConnectionResolution), GetParts().
//
// This matches StructuralSteelToolHelpers.AnyConnectionProviderInstalled() which returns
// false for the same reason. RevitCortex never compiles provider implementations from MCP
// input — these are discovery/reporting only.
// =====================================================================================

/// <summary>
/// Reports the structural-connection provider registry. The Revit API exposes no public way
/// to enumerate registered providers, so this returns availability=false with an explanatory
/// note rather than throwing. Read-only.
/// </summary>
[ToolSafety(true, false)]
public class GetStructuralConnectionProviderRegistryTool : ICortexTool
{
    public string Name => "get_structural_connection_provider_registry";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report registered structural connection providers (e.g. Autodesk Steel Connections, IDEA StatiCa). NOTE: the Revit API exposes no public query on StructuralConnectionsProviderRegistry, so this reports availability=false with a note when providers cannot be enumerated — it never fabricates a list.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // StructuralConnectionsProviderRegistry has no public query method or constructor
        // (only Dispose()/IsValidObject), so providers are not enumerable from the API.
        bool detectable = StructuralSteelToolHelpers.AnyConnectionProviderInstalled();
        return CortexResult<object>.Ok(new
        {
            count = 0,
            available = detectable,
            providers = new object[0],
            note = "The Revit API exposes no public method to enumerate the structural connection provider registry " +
                   "(StructuralConnectionsProviderRegistry has only Dispose()/IsValidObject). Provider presence cannot be " +
                   "queried programmatically; typed connection tools surface a ProviderUnavailable error at call time instead."
        });
    }
}

/// <summary>
/// Reports a single structural-connection provider's data. The provider data buffer
/// (StructuralConnectionsProviderData) is opaque and provider-filled via callback, with no
/// public reader, so this reports unavailability rather than throwing. Read-only.
/// </summary>
[ToolSafety(true, false)]
public class GetStructuralConnectionProviderDataTool : ICortexTool
{
    public string Name => "get_structural_connection_provider_data";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report a structural connection provider's metadata/capabilities. NOTE: StructuralConnectionsProviderData is an opaque provider-filled buffer with no public reader in the Revit API, so this reports available=false with a note — it never fabricates provider data.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var providerKey = input["providerId"]?.Value<string>() ?? input["providerKey"]?.Value<string>();
        return CortexResult<object>.Ok(new
        {
            providerId = providerKey,
            available = false,
            note = "StructuralConnectionsProviderData exposes no public accessors (only Dispose()/IsValidObject); it is filled " +
                   "by the provider through an internal callback and cannot be read back via the API. No provider metadata is " +
                   "retrievable programmatically."
        });
    }
}

/// <summary>
/// Reports validation detail for a placed connection. The Revit API has no public producer of a
/// populated ConnectionValidationInfo for an existing handler, so this returns the handler's own
/// CodeCheckingStatus (the only readable validation state) plus an explanatory note. Read-only.
/// </summary>
[ToolSafety(true, false)]
public class GetStructuralConnectionValidationInfoTool : ICortexTool
{
    public string Name => "get_structural_connection_validation_info";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report validation detail for a placed structural connection (connectionId). NOTE: the Revit API exposes no public producer of a populated ConnectionValidationInfo for an existing handler, so this returns the handler's CodeCheckingStatus (NotCalculated/OkChecked/CheckingFailed) and a note — it does not fabricate warnings.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var docResult = ToolHelpers.RequireDocument(session);
        var doc = docResult.Item1;
        if (docResult.Item2 != null) return docResult.Item2;

        var (handler, error) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (error != null) return error;

        string status = "Unknown";
        try { status = handler!.CodeCheckingStatus.ToString(); } catch { }

        return CortexResult<object>.Ok(new
        {
            connectionId = ToolHelpers.GetElementIdValue(handler!),
            codeCheckingStatus = status,
            warnings = new object[0],
            validationInfoAvailable = false,
            note = "No public Revit API produces a populated ConnectionValidationInfo for a placed handler (it is filled " +
                   "internally during provider-driven validation). Reporting the handler's CodeCheckingStatus instead; " +
                   "ConnectionValidationWarning detail (Reason: Alignment/Size/Shape/Connectivity, Resolution) is not retrievable here."
        });
    }
}
