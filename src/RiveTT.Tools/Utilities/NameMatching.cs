using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Text matching for user-supplied names — parameters, materials, types,
/// categories. Deliberately free of any Revit type so it is unit-testable
/// without a Revit host.
/// </summary>
public static class NameMatching
{
    /// <summary>
    /// Lowercase, accent-stripped, punctuation-collapsed form used for matching.
    /// "Hauteur d'allège", "hauteur d allege" and "HAUTEUR D ALLEGE" all normalize
    /// to the same string, which is what makes an English/French alias table
    /// usable on a localized document.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decomposed = value!.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Candidates an unresolved name could plausibly have meant. A candidate that
    /// CONTAINS the request ranks first: for "Repère" on a project whose only such
    /// parameter is the project parameter "ARC_PAR_Repère", containment is the
    /// answer the caller needs, and edit distance alone buries it.
    /// </summary>
    public static List<string> Suggest(string requestedName, IEnumerable<string> availableNames, int max = 5)
    {
        if (string.IsNullOrWhiteSpace(requestedName)) return new List<string>();
        var target = Normalize(requestedName);
        if (target.Length == 0) return new List<string>();

        return availableNames
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate =>
            {
                var normalized = Normalize(candidate);
                return new
                {
                    candidate,
                    score = Distance(target, normalized),
                    contains = normalized.Contains(target, StringComparison.Ordinal)
                };
            })
            .Where(entry => entry.contains || entry.score <= Math.Max(3, target.Length / 2))
            .OrderBy(entry => entry.contains ? 0 : 1)
            .ThenBy(entry => entry.score)
            .ThenBy(entry => entry.candidate, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(entry => entry.candidate)
            .ToList();
    }

    /// <summary>Levenshtein distance with two rolling rows.</summary>
    public static int Distance(string left, string right)
    {
        if (left == right) return 0;
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            Array.Copy(current, previous, current.Length);
        }

        return previous[right.Length];
    }
}
