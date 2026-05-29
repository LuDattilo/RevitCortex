using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Parameters;

/// <summary>
/// Lists, creates, reads, updates, or deletes global parameters in the project.
/// Global parameters are project-level named values that can drive dimensions and constraints.
/// </summary>
public class ManageGlobalParametersTool : ICortexTool
{
    public string Name => "manage_global_parameters";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, creates, reads, updates, or deletes global parameters. Actions: list, get, create, set, delete.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        if (!GlobalParametersManager.AreGlobalParametersAllowed(doc))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Global parameters are not supported in this document type (families not supported)");

        var action = input["action"]?.Value<string>() ?? "list";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list"        => ListGlobalParameters(doc),
                "get"         => GetGlobalParameter(doc, input),
                "create"      => CreateGlobalParameter(doc, input),
                "set"         => SetGlobalParameterValue(doc, input),
                "delete"      => DeleteGlobalParameter(doc, input, session),
                "rename"      => RenameGlobalParameter(doc, input),
                "set_formula" => SetGlobalParameterFormula(doc, input),
                "move_up"     => ReorderGlobalParameter(doc, input, up: true),
                "move_down"   => ReorderGlobalParameter(doc, input, up: false),
                "sort"        => SortGlobalParameters(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use one of: list, get, create, set, delete, rename, set_formula, move_up, move_down, sort")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to manage global parameters: {ex.Message}");
        }
    }

    private static CortexResult<object> ListGlobalParameters(Document doc)
    {
        var paramIds = GlobalParametersManager.GetAllGlobalParameters(doc);
        var parameters = paramIds
            .Select(id => doc.GetElement(id) as GlobalParameter)
            .Where(gp => gp != null)
            .Select(gp => BuildParameterInfo(gp!))
            .ToList();

        return CortexResult<object>.Ok(new
        {
            parameterCount = parameters.Count,
            parameters
        });
    }

    private static CortexResult<object> GetGlobalParameter(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        return CortexResult<object>.Ok(BuildParameterInfo(gp));
    }

    private static CortexResult<object> CreateGlobalParameter(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        if (FindByName(doc, name!) != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"A global parameter named '{name}' already exists");

        var dataType    = input["dataType"]?.Value<string>() ?? "text";
        var initialValue = input["value"]?.Value<string>();

        using var tx = new Transaction(doc, "RevitCortex: Create Global Parameter");
        tx.Start();

#if REVIT2023_OR_GREATER
        var gp = GlobalParameter.Create(doc, name, ResolveSpecTypeId(dataType));
#else
        var gp = GlobalParameter.Create(doc, name, ResolveParameterType(dataType));
#endif

        if (!string.IsNullOrEmpty(initialValue))
            ApplyStringValue(gp, initialValue!);

        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "create",
            name = gp.Name,
            elementId = ToolHelpers.GetElementIdValue(gp.Id),
            dataType
        });
    }

    private static CortexResult<object> SetGlobalParameterValue(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var value = input["value"]?.Value<string>();
        if (value == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "value is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        if (!string.IsNullOrEmpty(GetFormula(gp)))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Parameter '{name}' is driven by a formula and cannot be set directly");

        using var tx = new Transaction(doc, "RevitCortex: Set Global Parameter Value");
        tx.Start();
        ApplyStringValue(gp, value);
        tx.Commit();

        return CortexResult<object>.Ok(new { action = "set", name, value });
    }

    private static CortexResult<object> DeleteGlobalParameter(Document doc, JObject input, CortexSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        if (!session.RequestConfirmation("delete global parameter", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Delete Global Parameter");
        tx.Start();
        doc.Delete(gp.Id);
        tx.Commit();

        return CortexResult<object>.Ok(new { action = "delete", name });
    }

    /// <summary>
    /// Renames a global parameter. Unlike shared/project parameters, the
    /// GlobalParameter.Name setter is writable, so this is supported.
    /// </summary>
    private static CortexResult<object> RenameGlobalParameter(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Both name and newName are required for rename.");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        if (!GlobalParametersManager.IsUniqueName(doc, newName!))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"A global parameter named '{newName}' already exists.");

        using var tx = new Transaction(doc, "RevitCortex: Rename Global Parameter");
        tx.Start();
        gp.Name = newName;
        tx.Commit();

        return CortexResult<object>.Ok(new { action = "rename", oldName = name, newName });
    }

    /// <summary>
    /// Sets (or clears, with an empty string) the formula driving a global
    /// parameter. A formula makes the parameter's value read-only.
    /// </summary>
    private static CortexResult<object> SetGlobalParameterFormula(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        // formula may be empty string to clear; treat missing (null) as an error.
        if (input["formula"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "formula is required for set_formula (pass an empty string to clear the formula).");
        var formula = input["formula"]!.Value<string>() ?? "";

        var gp = FindByName(doc, name!);
        if (gp == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        using var tx = new Transaction(doc, "RevitCortex: Set Global Parameter Formula");
        tx.Start();
        try
        {
            gp.SetFormula(formula);
            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Revit rejected the formula: {ex.Message}",
                suggestion: "Check the formula syntax and that referenced parameters exist and don't create a circular reference.");
        }

        return CortexResult<object>.Ok(new
        {
            action = "set_formula",
            name,
            formula = string.IsNullOrEmpty(formula) ? null : formula,
            cleared = string.IsNullOrEmpty(formula)
        });
    }

    /// <summary>
    /// Moves a global parameter up or down in evaluation/display order. Ordering
    /// only shifts within the parameter's group.
    /// </summary>
    private static CortexResult<object> ReorderGlobalParameter(Document doc, JObject input, bool up)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        bool moved;
        using (var tx = new Transaction(doc, "RevitCortex: Reorder Global Parameter"))
        {
            tx.Start();
            moved = up
                ? GlobalParametersManager.MoveParameterUpOrder(doc, gp.Id)
                : GlobalParametersManager.MoveParameterDownOrder(doc, gp.Id);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            action = up ? "move_up" : "move_down",
            name,
            moved,
            message = moved ? null : "Already at the boundary of its group; no move performed."
        });
    }

    /// <summary>
    /// Sorts all global parameters ascending or descending (within each group).
    /// </summary>
    private static CortexResult<object> SortGlobalParameters(Document doc, JObject input)
    {
        var order = (input["order"]?.Value<string>() ?? "ascending").ToLowerInvariant();
        var sortOrder = order == "descending" || order == "desc"
            ? ParametersOrder.Descending
            : ParametersOrder.Ascending;

        using (var tx = new Transaction(doc, "RevitCortex: Sort Global Parameters"))
        {
            tx.Start();
            GlobalParametersManager.SortParameters(doc, sortOrder);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new { action = "sort", order = sortOrder.ToString() });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static GlobalParameter? FindByName(Document doc, string name)
    {
        return GlobalParametersManager.GetAllGlobalParameters(doc)
            .Select(id => doc.GetElement(id) as GlobalParameter)
            .FirstOrDefault(gp => gp?.Name == name);
    }

    private static object BuildParameterInfo(GlobalParameter gp)
    {
        string valueStr  = "";
        string valueType = "unknown";

        try
        {
            var val = gp.GetValue();
            switch (val)
            {
                case DoubleParameterValue  dpv: valueStr = dpv.Value.ToString("F6");  valueType = "double";    break;
                case StringParameterValue  spv: valueStr = spv.Value ?? "";            valueType = "string";    break;
                case IntegerParameterValue ipv: valueStr = ipv.Value.ToString();       valueType = "integer";   break;
                case ElementIdParameterValue epv:
#if REVIT2024_OR_GREATER
                    valueStr = epv.Value.Value.ToString();
#else
                    valueStr = epv.Value.IntegerValue.ToString();
#endif
                    valueType = "elementId";
                    break;
            }
        }
        catch { /* parameter may not have a value */ }

        return new
        {
            elementId  = ToolHelpers.GetElementIdValue(gp.Id),
            name       = gp.Name,
            formula    = GetFormula(gp),
            valueType,
            value = valueStr
        };
    }

    private static void ApplyStringValue(GlobalParameter gp, string value)
    {
        try
        {
            var current = gp.GetValue();
            switch (current)
            {
                case DoubleParameterValue:
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var dv))
                        gp.SetValue(new DoubleParameterValue(dv));
                    break;
                case StringParameterValue:
                    gp.SetValue(new StringParameterValue(value));
                    break;
                case IntegerParameterValue:
                    if (int.TryParse(value, out var iv))
                        gp.SetValue(new IntegerParameterValue(iv));
                    break;
            }
        }
        catch
        {
            // If GetValue fails (e.g. brand-new param with no type info yet), try string
            gp.SetValue(new StringParameterValue(value));
        }
    }

    /// Safely retrieve formula (API surface changed across versions; use reflection as fallback).
    private static string GetFormula(GlobalParameter gp)
    {
        try
        {
            // Revit 2016-2024: GetFormula() method
            var method = typeof(GlobalParameter).GetMethod("GetFormula",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (method != null)
                return (string?)method.Invoke(gp, null) ?? "";

            // Fallback: Formula property
            var prop = typeof(GlobalParameter).GetProperty("Formula");
            return (string?)prop?.GetValue(gp) ?? "";
        }
        catch { return ""; }
    }

#if REVIT2023_OR_GREATER
    private static ForgeTypeId ResolveSpecTypeId(string dataType) =>
        dataType.ToLowerInvariant() switch
        {
            "text"   or "string"          => SpecTypeId.String.Text,
            "integer" or "int"            => SpecTypeId.Int.Integer,
            "number"  or "double" or "real" => SpecTypeId.Number,
            "length"                      => SpecTypeId.Length,
            "area"                        => SpecTypeId.Area,
            "volume"                      => SpecTypeId.Volume,
            "angle"                       => SpecTypeId.Angle,
            "yesno"   or "boolean" or "bool" => SpecTypeId.Boolean.YesNo,
            _                             => SpecTypeId.String.Text
        };
#else
    private static ParameterType ResolveParameterType(string dataType) =>
        dataType.ToLowerInvariant() switch
        {
            "text"    or "string"            => ParameterType.Text,
            "integer" or "int"               => ParameterType.Integer,
            "number"  or "double" or "real"  => ParameterType.Number,
            "length"                         => ParameterType.Length,
            "area"                           => ParameterType.Area,
            "volume"                         => ParameterType.Volume,
            "angle"                          => ParameterType.Angle,
            "yesno"   or "boolean" or "bool" => ParameterType.YesNo,
            _                                => ParameterType.Text
        };
#endif
}
