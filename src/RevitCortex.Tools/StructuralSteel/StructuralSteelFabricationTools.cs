using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Steel;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

// =====================================================================================
// Module 4 — Fabrication metadata (5 tools). RE-SCOPED from 13.
//
// API VERIFIED 2026-05-30 (MetadataLoadContext reflection over RevitAPI.dll R25).
// Autodesk.Revit.DB.Steel.SteelElementProperties (base APIObject) exposes ONLY:
//   STATIC IList<ElementId> AddFabricationInformationForRevitElements(Document, IList<ElementId>)
//   STATIC Guid GetFabricationUniqueID(Document, Reference)
//   STATIC Reference GetReference(Document, Guid)
//   STATIC SteelElementProperties GetSteelElementProperties(Element)
//   PROP Guid UniqueID { get; set; }
//   PROP bool IsValidObject { get; }
// There is NO PostWarning/RemoveWarning/ClearWarnings/FlushWarnings/CountOfAsyncWarnings,
// NO RegisterMaterial, NO RemoveLink/AddToElement, NO GetExternalId/GetRevitId, NO SetChanged,
// and no public steel warning-queue / external-material / fabrication-link API. The plan's
// other 8 assumed members do not exist and their tools were cut. These 5 tools build only on
// the 6 real members. Reference.ElementId is a public {get;} ElementId property, so a Reference
// returned by GetReference is converted with ToolHelpers.GetElementIdValue(reference.ElementId).
// =====================================================================================

/// <summary>
/// Adds steel fabrication information to one or more Revit elements (SteelElementProperties).
/// Wraps the static SteelElementProperties.AddFabricationInformationForRevitElements; returns the
/// element ids that received fabrication info (the method returns the affected ids).
/// </summary>
public class AddSteelFabricationInfoTool : ICortexTool
{
    public string Name => "add_steel_fabrication_info";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Add steel fabrication information to Revit elements so they participate in steel detailing (SteelElementProperties). Provide elementIds (non-empty array). Supports dryRun. Returns the ids that received fabrication info plus any skipped ids.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        if (input["elementIds"] is not JArray arr || arr.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds (non-empty array) is required");

        var (ids, skipped) = StructuralSteelToolHelpers.ResolveElementIds(doc!, arr);
        if (ids.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No valid elements resolved",
                context: skipped.Count > 0 ? new Dictionary<string, object> { ["skipped"] = skipped } : null);

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                wouldAddTo = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                skipped
            });

        if (!session.RequestConfirmation("add steel fabrication info", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Add Steel Fabrication Info");
        tx.Start();
        try
        {
            var result = SteelElementProperties.AddFabricationInformationForRevitElements(doc!, ids);
            var affected = result?.Select(i => ToolHelpers.GetElementIdValue(i)).ToList() ?? new List<long>();
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Added steel fabrication info to {affected.Count} element(s)",
                elementIds = affected,
                skipped
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to add fabrication info: {ex.Message}");
        }
    }
}

/// <summary>
/// Reads the steel fabrication properties of an element: whether it has SteelElementProperties,
/// and the fabrication unique id (Guid string) if present.
/// </summary>
public class GetSteelElementFabricationPropertiesTool : ICortexTool
{
    public string Name => "get_steel_element_fabrication_properties";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the steel fabrication properties of an element: whether it has SteelElementProperties and its fabrication unique id (GUID string). Provide elementId. Returns hasFabricationProperties=false when the element carries no steel fabrication properties.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (elem, eerr) = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        if (eerr != null) return eerr;

        var elementId = ToolHelpers.GetElementIdValue(elem!);
        try
        {
            var props = SteelElementProperties.GetSteelElementProperties(elem!);
            bool has = props != null && props.IsValidObject;
            return CortexResult<object>.Ok(new
            {
                elementId,
                hasFabricationProperties = has,
                uniqueId = has ? props!.UniqueID.ToString() : null
            });
        }
        catch (Exception)
        {
            // The steel API can throw for elements that never participated in fabrication; treat as "no properties".
            return CortexResult<object>.Ok(new
            {
                elementId,
                hasFabricationProperties = false,
                uniqueId = (string?)null
            });
        }
    }
}

/// <summary>
/// Sets the steel fabrication unique id (SteelElementProperties.UniqueID) of an element.
/// Fails if the element has no steel fabrication properties (run add_steel_fabrication_info first).
/// </summary>
public class SetSteelFabricationUniqueIdTool : ICortexTool
{
    public string Name => "set_steel_fabrication_unique_id";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the steel fabrication unique id (GUID) of an element's SteelElementProperties. Provide elementId and uniqueId (a GUID). The element must already have steel fabrication properties (run add_steel_fabrication_info first).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (elem, eerr) = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        if (eerr != null) return eerr;

        var guid = StructuralSteelToolHelpers.ParseGuid(input["uniqueId"]?.Value<string>(), "uniqueId", out var guidError);
        if (guidError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, guidError);

        var elementId = ToolHelpers.GetElementIdValue(elem!);
        SteelElementProperties? props;
        try { props = SteelElementProperties.GetSteelElementProperties(elem!); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read steel element properties: {ex.Message}");
        }
        if (props == null || !props.IsValidObject)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"element {elementId} has no steel fabrication properties; run add_steel_fabrication_info first");

        // API VERIFIED 2026-05-30: SteelElementProperties.UniqueID exposes a PUBLIC getter but a
        // NON-PUBLIC setter (publicGet=True, publicSet=False), so `props.UniqueID = guid` does not
        // compile. The setter physically exists on the runtime assembly, so we invoke it reflectively
        // at this single call site (the verification mandate's "adapt only the offending call site").
        // If a future Revit removes/renames the setter, we surface a structured Fail rather than crash.
        var setter = typeof(SteelElementProperties)
            .GetProperty("UniqueID", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            ?.GetSetMethod(nonPublic: true);
        if (setter == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "SteelElementProperties.UniqueID is not settable in this Revit version (no accessible setter).",
                suggestion: "Use get_steel_fabrication_unique_id to read the id; setting it is not supported here.");

        if (!session.RequestConfirmation("set steel fabrication unique id", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Steel Fabrication Unique Id");
        tx.Start();
        try
        {
            setter.Invoke(props, new object[] { guid });
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Set steel fabrication unique id on element {elementId}",
                elementId,
                uniqueId = guid.ToString(),
                experimental = true,
                note = "Relies on a non-public SteelElementProperties.UniqueID setter invoked via reflection; may fail on future Revit versions."
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            // Unwrap TargetInvocationException so the caller sees the real Revit message.
            var msg = (ex as System.Reflection.TargetInvocationException)?.InnerException?.Message ?? ex.Message;
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set fabrication unique id: {msg}");
        }
    }
}

/// <summary>
/// Reads the steel fabrication unique id (SteelElementProperties.UniqueID) of an element.
/// (The reference-based GetFabricationUniqueID overload is not exposed because a Reference cannot be
/// fabricated from JSON; the element-properties path is used instead.)
/// </summary>
public class GetSteelFabricationUniqueIdTool : ICortexTool
{
    public string Name => "get_steel_fabrication_unique_id";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the steel fabrication unique id (GUID) of an element from its SteelElementProperties. Provide elementId. Returns a note when the element has no steel fabrication properties.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (elem, eerr) = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        if (eerr != null) return eerr;

        var elementId = ToolHelpers.GetElementIdValue(elem!);
        try
        {
            var props = SteelElementProperties.GetSteelElementProperties(elem!);
            if (props == null || !props.IsValidObject)
                return CortexResult<object>.Ok(new
                {
                    elementId,
                    uniqueId = (string?)null,
                    note = "Element has no steel fabrication properties; run add_steel_fabrication_info first."
                });
            return CortexResult<object>.Ok(new
            {
                elementId,
                uniqueId = props.UniqueID.ToString()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read steel fabrication unique id: {ex.Message}");
        }
    }
}

/// <summary>
/// Resolves the Revit element referenced by a steel fabrication GUID (SteelElementProperties.GetReference).
/// </summary>
public class GetSteelReferenceByFabricationIdTool : ICortexTool
{
    public string Name => "get_steel_reference_by_fabrication_id";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Resolve the Revit element referenced by a steel fabrication GUID. Provide fabricationGuid (a GUID). Returns found=true with the referenced elementId, or found=false when no element matches.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var guid = StructuralSteelToolHelpers.ParseGuid(input["fabricationGuid"]?.Value<string>(), "fabricationGuid", out var guidError);
        if (guidError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, guidError);

        try
        {
            var reference = SteelElementProperties.GetReference(doc!, guid);
            if (reference == null)
                return CortexResult<object>.Ok(new
                {
                    fabricationGuid = guid.ToString(),
                    found = false,
                    note = "No element is associated with this fabrication GUID."
                });
            return CortexResult<object>.Ok(new
            {
                fabricationGuid = guid.ToString(),
                found = true,
                elementId = ToolHelpers.GetElementIdValue(reference.ElementId)
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Ok(new
            {
                fabricationGuid = guid.ToString(),
                found = false,
                note = $"Could not resolve a reference for this fabrication GUID: {ex.Message}"
            });
        }
    }
}
