using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Resolves a requested schedule field name to a <see cref="SchedulableField"/>,
/// independently of the document language, and — when it cannot — says WHICH of
/// the two very different problems occurred.
///
/// Why: create_schedule/modify_schedule compared the requested name to
/// <c>SchedulableField.GetName(doc)</c>, which is localized, and reported every
/// miss as "NotSchedulableForCategory". On a French project, asking for "Mark"
/// produced "not schedulable for this category" while "Repère" worked — the
/// diagnosis pointed at a Revit limitation when the only problem was the language
/// of the name.
/// </summary>
public static class SchedulableFieldResolver
{
    /// <summary>The name matched nothing, in any language or alias.</summary>
    public const string ReasonNameNotFound = "ParameterNameNotFound";

    /// <summary>The parameter is real but Revit does not offer it for this category.</summary>
    public const string ReasonNotSchedulable = "NotSchedulableForCategory";

    public sealed class Resolution
    {
        public SchedulableField? Field { get; init; }
        public string? MatchedName { get; init; }
        public string? MatchedBy { get; init; }
        public string? Reason { get; init; }
        public List<string> Suggestions { get; init; } = new();
        public bool Success => Field != null;
    }

    public static Resolution Resolve(
        Document doc, IList<SchedulableField> schedulableFields, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return new Resolution { Reason = ReasonNameNotFound };

        var requested = requestedName.Trim();
        var named = schedulableFields
            .Select(field => new { field, name = SafeName(doc, field) })
            .Where(entry => !string.IsNullOrEmpty(entry.name))
            .ToList();

        // 1. Exact localized name.
        var exact = named.FirstOrDefault(entry =>
            entry.name.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return new Resolution { Field = exact.field, MatchedName = exact.name, MatchedBy = "display" };

        // 2. Accent/case-insensitive name.
        var normalizedRequest = ParameterNameResolver.Normalize(requested);
        var normalized = named.FirstOrDefault(entry =>
            ParameterNameResolver.Normalize(entry.name) == normalizedRequest);
        if (normalized != null)
            return new Resolution { Field = normalized.field, MatchedName = normalized.name, MatchedBy = "normalized" };

        // 3. English/French alias -> BuiltInParameter -> field parameter id.
        if (ParameterNameResolver.TryGetAliasCandidates(requested, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                var byParameter = named.FirstOrDefault(entry =>
                    ParameterIdValue(entry.field) == (long)(int)candidate);
                if (byParameter != null)
                    return new Resolution
                    {
                        Field = byParameter.field,
                        MatchedName = byParameter.name,
                        MatchedBy = $"alias:{candidate}"
                    };
            }

            // The alias is known, so the caller did not mistype: this really is a
            // category limitation rather than a naming problem.
            return new Resolution
            {
                Reason = ReasonNotSchedulable,
                Suggestions = ParameterNameResolver.Suggest(requested, named.Select(entry => entry.name))
            };
        }

        return new Resolution
        {
            Reason = ReasonNameNotFound,
            Suggestions = ParameterNameResolver.Suggest(requested, named.Select(entry => entry.name))
        };
    }

    /// <summary>Human-readable explanation attached to a skipped field.</summary>
    public static string Explain(string reason, string requestedName)
    {
        return reason == ReasonNotSchedulable
            ? $"'{requestedName}' is a known parameter but Revit does not expose it as a schedulable field " +
              "for this category."
            : $"No schedulable field matches '{requestedName}' in this document " +
              "(the document language may differ; try the suggestions or list_schedulable_fields).";
    }

    private static string SafeName(Document doc, SchedulableField field)
    {
        try { return field.GetName(doc) ?? ""; }
        catch { return ""; }
    }

    private static long ParameterIdValue(SchedulableField field)
    {
        try
        {
            return field.ParameterId.Value;
        }
        catch
        {
            return 0;
        }
    }
}
