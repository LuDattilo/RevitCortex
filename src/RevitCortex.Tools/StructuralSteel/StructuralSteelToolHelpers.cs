using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>
/// Shared helpers for all StructuralSteel tools. Pure parsers carry no Document dependency and are
/// unit-tested in SteelHelpersParsingTests. Revit-dependent resolvers require a Document; their bodies
/// are verified against the reflected Nice3point ref API.
/// </summary>
public static class StructuralSteelToolHelpers
{
    public const double MmPerFoot = 304.8;
    public static double ToMm(double feet) => feet * MmPerFoot;
    public static double FromMm(double mm) => mm / MmPerFoot;

    public static TEnum ParseEnum<TEnum>(string? value, string fieldName, out string? error)
        where TEnum : struct, Enum
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"'{fieldName}' is required. Valid values: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}";
            return default;
        }
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        error = $"Invalid '{fieldName}' = '{value}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}";
        return default;
    }

    public static Guid ParseGuid(string? value, string fieldName, out string? error)
    {
        error = null;
        if (Guid.TryParse(value, out var g)) return g;
        error = $"Invalid '{fieldName}' = '{value}'. Expected a GUID like 00000000-0000-0000-0000-000000000000.";
        return Guid.Empty;
    }

    public static long[] ParseLongArray(JArray arr, string fieldName, out string? error)
    {
        error = null;
        var outList = new List<long>();
        foreach (var t in arr)
        {
            var v = t.Type == JTokenType.Integer ? t.Value<long?>() : null;
            if (v == null) { error = $"'{fieldName}' must be an array of integer element ids."; return outList.ToArray(); }
            outList.Add(v.Value);
        }
        return outList.ToArray();
    }

    public enum ConnectionInputAction { AddElementIds, RemoveElementIds, AddReferences, RemoveReferences }

    public static ConnectionInputAction ParseConnectionInputAction(string? value, out string? error)
    {
        error = null;
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "add_element_ids": return ConnectionInputAction.AddElementIds;
            case "remove_element_ids": return ConnectionInputAction.RemoveElementIds;
            case "add_references": return ConnectionInputAction.AddReferences;
            case "remove_references": return ConnectionInputAction.RemoveReferences;
            default:
                error = "Invalid 'action'. Valid: add_element_ids, remove_element_ids, add_references, remove_references";
                return ConnectionInputAction.AddElementIds;
        }
    }

    public static XYZ ParseXyzMm(JToken token)
    {
        var x = token["x"]?.Value<double?>() ?? 0;
        var y = token["y"]?.Value<double?>() ?? 0;
        var z = token["z"]?.Value<double?>() ?? 0;
        return new XYZ(FromMm(x), FromMm(y), FromMm(z));
    }

    public static JObject XyzToDtoMm(XYZ p) => new JObject { ["x"] = ToMm(p.X), ["y"] = ToMm(p.Y), ["z"] = ToMm(p.Z) };

    public static (Element? element, CortexResult<object>? error) RequireElement(Document doc, long? id)
    {
        if (id == null || id <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "an element id is required"));
        var e = doc.GetElement(ToolHelpers.ToElementId(id.Value));
        if (e == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {id}"));
        return (e, null);
    }

    public static (StructuralConnectionHandler? handler, CortexResult<object>? error) RequireConnectionHandler(Document doc, long? id)
    {
        if (id == null || id <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "connectionId is required"));
        var h = doc.GetElement(ToolHelpers.ToElementId(id.Value)) as StructuralConnectionHandler;
        if (h == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No structural connection handler with id {id}",
                suggestion: "Use list_steel_connection_handlers to find connection ids"));
        return (h, null);
    }

    public static (IList<ElementId> ids, IList<long> skipped) ResolveElementIds(Document doc, JArray arr)
    {
        var ids = new List<ElementId>(); var skipped = new List<long>();
        foreach (var t in arr.Where(x => x.Type == JTokenType.Integer))
        {
            var raw = t.Value<long>();
            var eid = ToolHelpers.ToElementId(raw);
            if (doc.GetElement(eid) != null) ids.Add(eid); else skipped.Add(raw);
        }
        return (ids, skipped);
    }

    public static CortexResult<object> MinVersionError(string feature, int minYear)
        => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            $"{feature} requires Revit {minYear} or newer; the active target does not support it.",
            suggestion: $"Open the model in Revit {minYear}+ to use this feature.");

    public static CortexResult<object> ProviderUnavailableError(string feature)
        => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            $"{feature} requires an installed structural steel connection provider (e.g. Autodesk Steel Connections, IDEA StatiCa) and steel-compatible content.",
            suggestion: "Use create_generic_steel_connection when provider availability is unknown, or check get_structural_steel_api_capabilities.");

    /// <summary>
    /// Best-effort detection of whether any structural connection provider is installed.
    /// Reflected against Nice3point RevitAPI.dll 2023-2027: <c>StructuralConnectionsProviderRegistry</c>
    /// exposes NO public query method and NO public constructor (only <c>Dispose()</c> / <c>IsValidObject</c>),
    /// and <c>IStructuralConnectionsProvider</c> is a provider-implemented interface, not a queryable list.
    /// There is therefore no public API to enumerate or test for installed providers, so this returns
    /// <c>false</c>. The capabilities tool treats the flag as advisory only.
    /// </summary>
    public static bool AnyConnectionProviderInstalled()
    {
        // No queryable public registry exists; cannot affirm a provider is installed.
        return false;
    }
}
