using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Edits family type parameters in the BACKGROUND — no window opens, nothing
/// changes on screen — on the pattern export_families and list_family_sizes
/// already use in production: Document.EditFamily, modify, LoadFamily back into
/// the project, Close(false) in a finally.
///
/// Document.EditFamily does NOT deadlock from this connector's ExternalEvent
/// dispatcher; that was a false documentation claim, corrected as part of P4.1
/// in PLAN_CORRECTION.md. The Document EditFamily returns is deliberately never
/// activated in the Revit UI here — open_family covers visual editing, and
/// activating a background EditFamily document was the one segment of the
/// document-opening path PLAN_CORRECTION.md's Annex A did not measure.
///
/// Scope: only existing family TYPES and their parameter VALUES (dimensions,
/// materials, yes/no, text) — not new types, not geometry. That covers the
/// common case (retype a door/window family's dimensions across its catalog
/// without opening it) without the much larger surface of arbitrary family
/// editing.
/// </summary>
[ToolSafety(false, true)]
public sealed class EditFamilyTool : ICortexTool
{
    public string Name => "edit_family";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Edits a loaded family's type parameters in the background — no window opens. Pass familyId or " +
        "familyName, and changes: [{typeName, parameters: {paramName: value}}]. Only existing types and " +
        "parameter values (dimensions, materials, yes/no, text): not new types, not geometry. Numeric values " +
        "are internal units (feet); pass a string with a unit (e.g. \"900 mm\") to write a display value. " +
        "The project's copy of the family is updated in place (LoadFamily with overwrite) — nothing opens " +
        "on screen. Use open_family instead for visual/geometry edits.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var familyId = input["familyId"]?.Value<long>() ?? 0;
        var familyName = input["familyName"]?.Value<string>();
        var changesToken = input["changes"] as JArray;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (familyId <= 0 && string.IsNullOrWhiteSpace(familyName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "familyId or familyName is required",
                suggestion: "Read family ids from load_family(action: \"list\") or audit_families.");

        if (changesToken == null || changesToken.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "changes is required: [{\"typeName\": \"900x2100\", \"parameters\": {\"Width\": \"900 mm\"}}]");

        Family? family = familyId > 0
            ? doc.GetElement(ToolHelpers.ToElementId(familyId)) as Family
            : new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

        if (family == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                familyId > 0 ? $"familyId {familyId} is not a Family" : $"No family named '{familyName}'");

        if (family.IsInPlace)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"'{family.Name}' is an in-place family: Document.EditFamily does not support those.",
                suggestion: "Edit in-place families from the Revit UI (Edit In-Place).");

        if (!family.IsEditable)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"'{family.Name}' is not editable (a system family, or a family this document cannot open for editing).");

        var requests = new List<(string TypeName, Dictionary<string, JToken> Parameters)>();
        foreach (var entry in changesToken.OfType<JObject>())
        {
            var typeName = entry["typeName"]?.Value<string>();
            var parametersToken = entry["parameters"] as JObject;
            if (string.IsNullOrWhiteSpace(typeName) || parametersToken == null || !parametersToken.Properties().Any())
                continue;
            requests.Add((typeName!, parametersToken.Properties().ToDictionary(p => p.Name, p => p.Value)));
        }

        if (requests.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "changes did not contain any valid {typeName, parameters} entry");

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: would edit {requests.Count} type(s) of '{family.Name}' in the background " +
                          "and reload the family into this project (nothing opens on screen).",
                familyId = ToolHelpers.GetElementIdValue(family.Id),
                familyName = family.Name,
                requestedTypes = requests.Select(r => new { r.TypeName, parameterNames = r.Parameters.Keys.ToList() }).ToList()
            });
        }

        Document? famDoc = null;
        try
        {
            famDoc = doc.EditFamily(family);
            if (famDoc == null)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    $"Document.EditFamily returned null for '{family.Name}'");

            var manager = famDoc.FamilyManager;
            var results = new List<object>();
            var anySucceeded = false;

            using (var tx = new Transaction(famDoc, "RiveTT: Edit Family Types"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                foreach (var (typeName, parameters) in requests)
                {
                    var type = manager.Types.Cast<FamilyType>()
                        .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                    if (type == null)
                    {
                        results.Add(new { typeName, success = false, reason = "Type not found in the family" });
                        continue;
                    }

                    manager.CurrentType = type;
                    var paramResults = new List<object>();
                    foreach (var (paramName, value) in parameters)
                    {
                        var param = FindFamilyParameter(manager, paramName);
                        if (param == null)
                        {
                            paramResults.Add(new { parameterName = paramName, success = false, reason = "Parameter not found" });
                            continue;
                        }
                        if (param.IsReadOnly)
                        {
                            paramResults.Add(new { parameterName = paramName, success = false, reason = "Parameter is read-only" });
                            continue;
                        }

                        var set = TrySetFamilyParameterValue(manager, param, value, out var error);
                        paramResults.Add(new { parameterName = paramName, success = set, reason = set ? null : error });
                        if (set) anySucceeded = true;
                    }

                    results.Add(new { typeName, success = true, parameters = paramResults });
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the family edit: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            if (!anySucceeded)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    "None of the requested type/parameter changes could be applied. Nothing was reloaded into the project.",
                    context: new Dictionary<string, object> { ["results"] = results });

            // Push the edited family document back into the project it came
            // from — no file path involved, and nothing is left open on screen.
            var loadedFamily = famDoc.LoadFamily(doc, new OverwritingFamilyLoadOptions(overwrite: true));

            return CortexResult<object>.Ok(new
            {
                message = loadedFamily != null
                    ? $"Edited '{family.Name}' in the background and reloaded it into the project."
                    : $"Edited '{family.Name}' in the background, but Document.LoadFamily reported no change " +
                      "(the family may already have matched).",
                familyId = ToolHelpers.GetElementIdValue(family.Id),
                familyName = family.Name,
                reloaded = loadedFamily != null,
                results
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to edit the family: {exception.Message}");
        }
        finally
        {
            // Never leave the background family document open: it locks the
            // family's identity for the rest of the session — see P4.1 in
            // PLAN_CORRECTION.md (three residual family documents were left open
            // by the campaign that measured this gap).
            try { famDoc?.Close(false); } catch { }
        }
    }

    private static FamilyParameter? FindFamilyParameter(FamilyManager manager, string name)
    {
        foreach (FamilyParameter param in manager.Parameters)
        {
            if (param.Definition?.Name != null &&
                param.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return param;
        }
        return null;
    }

    private static bool TrySetFamilyParameterValue(FamilyManager manager, FamilyParameter param, JToken value, out string error)
    {
        error = "";
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    manager.Set(param, value.Value<string>() ?? "");
                    return true;
                case StorageType.Integer:
                    manager.Set(param, value.Value<int>());
                    return true;
                case StorageType.Double:
                    if (value.Type == JTokenType.String)
                    {
                        var text = (value.Value<string>() ?? "").Trim();
                        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var raw))
                            manager.Set(param, raw);
                        else
                            manager.SetValueString(param, text);
                    }
                    else
                    {
                        manager.Set(param, value.Value<double>());
                    }
                    return true;
                case StorageType.ElementId:
                    manager.Set(param, ToolHelpers.ToElementId(value.Value<long>()));
                    return true;
                default:
                    error = $"Unsupported storage type: {param.StorageType}";
                    return false;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
