using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Language-independent resolution of a free-text parameter name to a real
/// <see cref="Parameter"/> on an element.
///
/// Why this exists: every tool that accepts parameter names used to compare the
/// caller's string to <c>Definition.Name</c>, which Revit localizes. On a French
/// document, "Mark"/"Level"/"Width" silently matched nothing and the tools
/// returned empty columns — indistinguishable from "the parameter is empty".
/// Agents are trained on the English API names, so this was the single most
/// frequent failure mode of the connector.
///
/// Resolution order (first hit wins):
///   1. explicit BuiltInParameter (enum name or numeric value),
///   2. exact localized display name (LookupParameter, instance then type),
///   3. English/French alias table -> candidate BuiltInParameter list,
///   4. accent- and case-insensitive display-name match.
/// When everything fails the caller gets suggestions instead of a blank cell.
/// </summary>
public static class ParameterNameResolver
{
    /// <summary>
    /// English and French aliases mapped to the BuiltInParameter candidates that
    /// may carry them. Several candidates per alias is intentional: "Level" is
    /// FAMILY_LEVEL_PARAM on a hosted instance, ROOM_LEVEL_ID on a room and
    /// WALL_BASE_CONSTRAINT on a wall. Candidates are probed in order on the
    /// instance, then on its type.
    /// </summary>
    private static readonly Dictionary<string, BuiltInParameter[]> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mark"] = new[] { BuiltInParameter.ALL_MODEL_MARK },
            ["repere"] = new[] { BuiltInParameter.ALL_MODEL_MARK },
            ["type mark"] = new[] { BuiltInParameter.ALL_MODEL_TYPE_MARK },
            ["repere du type"] = new[] { BuiltInParameter.ALL_MODEL_TYPE_MARK },
            ["comments"] = new[] { BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS },
            ["commentaires"] = new[] { BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS },
            ["type comments"] = new[] { BuiltInParameter.ALL_MODEL_TYPE_COMMENTS },
            ["commentaires sur le type"] = new[] { BuiltInParameter.ALL_MODEL_TYPE_COMMENTS },
            ["description"] = new[] { BuiltInParameter.ALL_MODEL_DESCRIPTION },
            ["manufacturer"] = new[] { BuiltInParameter.ALL_MODEL_MANUFACTURER },
            ["fabricant"] = new[] { BuiltInParameter.ALL_MODEL_MANUFACTURER },
            ["model"] = new[] { BuiltInParameter.ALL_MODEL_MODEL },
            ["modele"] = new[] { BuiltInParameter.ALL_MODEL_MODEL },
            ["type name"] = new[] { BuiltInParameter.ALL_MODEL_TYPE_NAME, BuiltInParameter.SYMBOL_NAME_PARAM },
            ["nom du type"] = new[] { BuiltInParameter.ALL_MODEL_TYPE_NAME, BuiltInParameter.SYMBOL_NAME_PARAM },
            ["type"] = new[] { BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, BuiltInParameter.ALL_MODEL_TYPE_NAME },
            ["family"] = new[] { BuiltInParameter.ELEM_FAMILY_PARAM },
            ["famille"] = new[] { BuiltInParameter.ELEM_FAMILY_PARAM },
            ["family and type"] = new[] { BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM },
            ["famille et type"] = new[] { BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM },
            ["level"] = new[]
            {
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                BuiltInParameter.ROOM_LEVEL_ID,
                BuiltInParameter.WALL_BASE_CONSTRAINT,
                BuiltInParameter.LEVEL_PARAM,
            },
            ["niveau"] = new[]
            {
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                BuiltInParameter.ROOM_LEVEL_ID,
                BuiltInParameter.WALL_BASE_CONSTRAINT,
                BuiltInParameter.LEVEL_PARAM,
            },
            ["width"] = new[]
            {
                BuiltInParameter.DOOR_WIDTH,
                BuiltInParameter.WINDOW_WIDTH,
                BuiltInParameter.GENERIC_WIDTH,
                BuiltInParameter.FURNITURE_WIDTH,
                BuiltInParameter.WALL_ATTR_WIDTH_PARAM,
            },
            ["largeur"] = new[]
            {
                BuiltInParameter.DOOR_WIDTH,
                BuiltInParameter.WINDOW_WIDTH,
                BuiltInParameter.GENERIC_WIDTH,
                BuiltInParameter.FURNITURE_WIDTH,
                BuiltInParameter.WALL_ATTR_WIDTH_PARAM,
            },
            ["height"] = new[]
            {
                BuiltInParameter.DOOR_HEIGHT,
                BuiltInParameter.WINDOW_HEIGHT,
                BuiltInParameter.GENERIC_HEIGHT,
                BuiltInParameter.FURNITURE_HEIGHT,
                BuiltInParameter.WALL_USER_HEIGHT_PARAM,
            },
            ["hauteur"] = new[]
            {
                BuiltInParameter.DOOR_HEIGHT,
                BuiltInParameter.WINDOW_HEIGHT,
                BuiltInParameter.GENERIC_HEIGHT,
                BuiltInParameter.FURNITURE_HEIGHT,
                BuiltInParameter.WALL_USER_HEIGHT_PARAM,
            },
            ["unconnected height"] = new[] { BuiltInParameter.WALL_USER_HEIGHT_PARAM },
            ["hauteur sans contrainte"] = new[] { BuiltInParameter.WALL_USER_HEIGHT_PARAM },
            ["area"] = new[] { BuiltInParameter.ROOM_AREA, BuiltInParameter.HOST_AREA_COMPUTED },
            ["surface"] = new[] { BuiltInParameter.ROOM_AREA, BuiltInParameter.HOST_AREA_COMPUTED },
            ["volume"] = new[] { BuiltInParameter.ROOM_VOLUME, BuiltInParameter.HOST_VOLUME_COMPUTED },
            ["perimeter"] = new[] { BuiltInParameter.ROOM_PERIMETER },
            ["perimetre"] = new[] { BuiltInParameter.ROOM_PERIMETER },
            ["length"] = new[] { BuiltInParameter.CURVE_ELEM_LENGTH, BuiltInParameter.INSTANCE_LENGTH_PARAM },
            ["longueur"] = new[] { BuiltInParameter.CURVE_ELEM_LENGTH, BuiltInParameter.INSTANCE_LENGTH_PARAM },
            ["thickness"] = new[] { BuiltInParameter.WALL_ATTR_WIDTH_PARAM, BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM },
            ["epaisseur"] = new[] { BuiltInParameter.WALL_ATTR_WIDTH_PARAM, BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM },
            ["base constraint"] = new[] { BuiltInParameter.WALL_BASE_CONSTRAINT },
            ["contrainte inferieure"] = new[] { BuiltInParameter.WALL_BASE_CONSTRAINT },
            ["top constraint"] = new[] { BuiltInParameter.WALL_HEIGHT_TYPE },
            ["contrainte superieure"] = new[] { BuiltInParameter.WALL_HEIGHT_TYPE },
            ["base offset"] = new[] { BuiltInParameter.WALL_BASE_OFFSET },
            ["decalage inferieur"] = new[] { BuiltInParameter.WALL_BASE_OFFSET },
            ["top offset"] = new[] { BuiltInParameter.WALL_TOP_OFFSET },
            ["decalage superieur"] = new[] { BuiltInParameter.WALL_TOP_OFFSET },
            ["sill height"] = new[] { BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM },
            ["hauteur d allege"] = new[] { BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM },
            ["head height"] = new[] { BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM },
            ["hauteur du linteau"] = new[] { BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM },
            ["room bounding"] = new[] { BuiltInParameter.WALL_ATTR_ROOM_BOUNDING },
            ["limite de piece"] = new[] { BuiltInParameter.WALL_ATTR_ROOM_BOUNDING },
            ["number"] = new[] { BuiltInParameter.ROOM_NUMBER, BuiltInParameter.SHEET_NUMBER },
            ["numero"] = new[] { BuiltInParameter.ROOM_NUMBER, BuiltInParameter.SHEET_NUMBER },
            ["name"] = new[] { BuiltInParameter.ROOM_NAME, BuiltInParameter.SHEET_NAME, BuiltInParameter.VIEW_NAME },
            ["nom"] = new[] { BuiltInParameter.ROOM_NAME, BuiltInParameter.SHEET_NAME, BuiltInParameter.VIEW_NAME },
            ["department"] = new[] { BuiltInParameter.ROOM_DEPARTMENT },
            ["service"] = new[] { BuiltInParameter.ROOM_DEPARTMENT },
            ["occupancy"] = new[] { BuiltInParameter.ROOM_OCCUPANCY },
            ["phase created"] = new[] { BuiltInParameter.PHASE_CREATED },
            ["phase de creation"] = new[] { BuiltInParameter.PHASE_CREATED },
            ["phase demolished"] = new[] { BuiltInParameter.PHASE_DEMOLISHED },
            ["phase de demolition"] = new[] { BuiltInParameter.PHASE_DEMOLISHED },
            ["workset"] = new[] { BuiltInParameter.ELEM_PARTITION_PARAM },
            ["sous projet"] = new[] { BuiltInParameter.ELEM_PARTITION_PARAM },
            ["sheet name"] = new[] { BuiltInParameter.SHEET_NAME },
            ["nom de la feuille"] = new[] { BuiltInParameter.SHEET_NAME },
            ["sheet number"] = new[] { BuiltInParameter.SHEET_NUMBER },
            ["numero de feuille"] = new[] { BuiltInParameter.SHEET_NUMBER },
            ["view name"] = new[] { BuiltInParameter.VIEW_NAME },
            ["nom de la vue"] = new[] { BuiltInParameter.VIEW_NAME },
            ["scale"] = new[] { BuiltInParameter.VIEW_SCALE },
            ["echelle"] = new[] { BuiltInParameter.VIEW_SCALE },
        };

    /// <summary>
    /// Resolve <paramref name="requestedName"/> on <paramref name="element"/>,
    /// looking at the element then at its type. Returns null when nothing matches.
    /// </summary>
    public static Parameter? Resolve(Element element, string? requestedName, Document? document = null)
        => Resolve(element, requestedName, document, out _);

    /// <summary>
    /// Resolve and report how the match was obtained (<c>builtin</c>,
    /// <c>display</c>, <c>alias</c>, <c>normalized</c>) so responses can tell the
    /// caller that a localized name was used instead of the requested one.
    /// </summary>
    public static Parameter? Resolve(
        Element element, string? requestedName, Document? document, out string? matchedBy)
    {
        matchedBy = null;
        if (element == null || string.IsNullOrWhiteSpace(requestedName)) return null;

        var name = requestedName.Trim();
        var typeElement = GetTypeElement(element, document);

        // 1. Explicit BuiltInParameter (enum name or numeric).
        if (TryParseBuiltInParameter(name, out var explicitBip))
        {
            var direct = Probe(element, typeElement, explicitBip);
            if (direct != null)
            {
                matchedBy = "builtin";
                return direct;
            }
        }

        // 2. Exact localized display name.
        var byName = element.LookupParameter(name) ?? typeElement?.LookupParameter(name);
        if (byName != null)
        {
            matchedBy = "display";
            return byName;
        }

        // 3. Alias table (English or French) -> BuiltInParameter candidates.
        if (Aliases.TryGetValue(Normalize(name), out var candidates))
        {
            foreach (var candidate in candidates)
            {
                var hit = Probe(element, typeElement, candidate);
                if (hit != null)
                {
                    matchedBy = "alias";
                    return hit;
                }
            }
        }

        // 4. Accent- and case-insensitive display-name match.
        var normalized = Normalize(name);
        var fuzzy = FindByNormalizedName(element, normalized)
                    ?? (typeElement != null ? FindByNormalizedName(typeElement, normalized) : null);
        if (fuzzy != null)
        {
            matchedBy = "normalized";
            return fuzzy;
        }

        return null;
    }

    /// <summary>
    /// Names an unresolved request could plausibly have meant, ordered by
    /// containment then edit distance. Feeds the <c>unresolvedParameterNames</c>
    /// block tools return instead of a silent empty column.
    /// </summary>
    public static List<string> Suggest(
        string requestedName, IEnumerable<string> availableNames, int max = 5)
        => NameMatching.Suggest(requestedName, availableNames, max);

    /// <summary>
    /// Every parameter name visible on an element (instance + type), used to build
    /// suggestions.
    /// </summary>
    public static List<string> AvailableNames(Element element, Document? document = null)
    {
        var names = new List<string>();
        if (element == null) return names;

        foreach (Parameter parameter in element.Parameters)
        {
            var name = parameter.Definition?.Name;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
        }

        var typeElement = GetTypeElement(element, document);
        if (typeElement != null)
        {
            foreach (Parameter parameter in typeElement.Parameters)
            {
                var name = parameter.Definition?.Name;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
            }
        }

        return names;
    }

    /// <summary>
    /// Lowercase, accent-stripped, punctuation-collapsed form used for matching.
    /// See <see cref="NameMatching.Normalize"/>.
    /// </summary>
    public static string Normalize(string value) => NameMatching.Normalize(value);

    /// <summary>True when the alias table knows this name in either language.</summary>
    public static bool IsKnownAlias(string name) => Aliases.ContainsKey(Normalize(name));

    /// <summary>
    /// BuiltInParameter candidates an English or French alias may designate.
    /// Consumers that match on parameter ids rather than on an element (schedule
    /// fields, filters) need the candidates, not a resolved Parameter.
    /// </summary>
    public static bool TryGetAliasCandidates(string? name, out BuiltInParameter[] candidates)
    {
        candidates = Array.Empty<BuiltInParameter>();
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!Aliases.TryGetValue(Normalize(name!), out var found)) return false;
        candidates = found;
        return true;
    }

    private static Parameter? Probe(Element element, Element? typeElement, BuiltInParameter builtIn)
    {
        Parameter? parameter = null;
        try { parameter = element.get_Parameter(builtIn); } catch { }
        if (parameter != null) return parameter;
        if (typeElement == null) return null;
        try { return typeElement.get_Parameter(builtIn); } catch { return null; }
    }

    private static Parameter? FindByNormalizedName(Element element, string normalizedName)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            var name = parameter.Definition?.Name;
            if (name != null && Normalize(name) == normalizedName) return parameter;
        }
        return null;
    }

    private static Element? GetTypeElement(Element element, Document? document)
    {
        try
        {
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId) return null;
            return (document ?? element.Document)?.GetElement(typeId);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseBuiltInParameter(string value, out BuiltInParameter builtInParameter)
    {
        builtInParameter = default;
        var candidate = value.Trim();
        if (candidate.StartsWith("Autodesk.Revit.DB.BuiltInParameter.", StringComparison.OrdinalIgnoreCase))
            candidate = candidate.Substring("Autodesk.Revit.DB.BuiltInParameter.".Length);
        else if (candidate.StartsWith("BuiltInParameter.", StringComparison.OrdinalIgnoreCase))
            candidate = candidate.Substring("BuiltInParameter.".Length);

        if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            builtInParameter = (BuiltInParameter)numeric;
            return Enum.IsDefined(typeof(BuiltInParameter), builtInParameter);
        }

        return Enum.TryParse(candidate, true, out builtInParameter)
               && Enum.IsDefined(typeof(BuiltInParameter), builtInParameter);
    }
}
