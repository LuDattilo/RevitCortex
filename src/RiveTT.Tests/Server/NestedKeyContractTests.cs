using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// Guards the keys announced INSIDE a description rather than as parameters.
///
/// ServerRuntimeParameterContractTests checks top-level parameters only, and that is a
/// real blind spot: the richest tools take one JSON-array parameter and document its
/// shape in prose — "[{number, name, viewIds?}]". Those inner keys are a contract too,
/// and nothing was checking them.
///
/// Two shipped defects came through this gap and both were silent:
///
///   create_dimensions advertised referenceIds while the runtime read elementIds, so
///   every documented element-mode call fell through to the "provide either..." warning
///   and created nothing.
///
///   workflow_sheet_set advertised viewIds and never read it, so whole sheet sets came
///   out empty and were reported as a success.
///
/// The check is deliberately lenient about WHERE a key is read — the same runtime tool,
/// or any shared helper — because the question is "does anything read it", not "is it
/// read tidily". A key read nowhere in RiveTT.Tools cannot possibly take effect.
/// </summary>
public class NestedKeyContractTests
{
    private static readonly Regex McpToolAttribute =
        new(@"\[McpServerTool\(Name\s*=\s*""([a-z0-9_]+)""\)", RegexOptions.Compiled);

    private static readonly Regex ExecuteTarget =
        new(@"ExecuteAsync\(\s*""([a-z0-9_]+)""", RegexOptions.Compiled);

    private static readonly Regex RuntimeToolName =
        new(@"public\s+string\s+Name\s*=>\s*""([a-z0-9_]+)""", RegexOptions.Compiled);

    /// <summary>Any string literal in the runtime: how a JSON key is always read.</summary>
    private static readonly Regex StringLiteral =
        new("\"([A-Za-z0-9_]+)\"", RegexOptions.Compiled);

    /// <summary>A brace group inside a description: [{number, name, viewIds?}].</summary>
    private static readonly Regex BraceGroup =
        new(@"\{([^{}]{2,240})\}", RegexOptions.Compiled);

    /// <summary>
    /// The text of a [Description("...")] attribute, including its concatenated parts.
    /// Only these are scanned — running the brace matcher over the whole wrapper body
    /// picked up C# blocks and reported local variables ("var request = new JObject();")
    /// as undocumented keys.
    /// </summary>
    private static readonly Regex DescriptionText =
        new(@"Description\(\s*((?:@?""(?:[^""\\]|\\.)*""\s*\+?\s*)+)\)", RegexOptions.Compiled);

    /// <summary>
    /// A public auto-property, i.e. a JSON key bound through a typed DTO rather than an
    /// indexer. set_element_workset reads its keys via ToObject&lt;SetWorksetRequest&gt;(),
    /// so "worksetName" never appears as a string literal anywhere.
    /// </summary>
    private static readonly Regex DtoProperty =
        new(@"public\s+[A-Za-z0-9_<>\[\]\?\.]+\s+([A-Z][A-Za-z0-9_]*)\s*\{\s*get;", RegexOptions.Compiled);

    /// <summary>
    /// Words that appear inside brace groups without being JSON keys: types, units,
    /// placeholders and prose. Mirrors the NOISE set in tools/audit-tool-surface.py.
    /// </summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "number", "name", "value", "true", "false", "null", "string", "int", "double",
        "bool", "json", "array", "object", "etc", "optional", "default", "mm", "deg",
        "and", "or", "the", "for", "with", "each", "one", "per", "see", "use", "eg",
        "elementId", "elementIds", "id", "ids",
    };

    /// <summary>
    /// Keys that are genuinely documentation-only, with the reason. Anything else that
    /// is announced and never read is a bug, not an entry for this table.
    /// </summary>
    private static readonly Dictionary<string, string> DocumentationOnly = new(StringComparer.Ordinal)
    {
        // These two read a free-form JObject of caller-chosen names; the key in the
        // description is a PLACEHOLDER standing for "whatever parameter you mean", not a
        // literal the runtime could ever look for.
        ["duplicate_family_type.paramName"] =
            "parameterOverrides is a free-form {name: value} map; paramName is the placeholder",
        ["sync_csv_parameters.paramName1"] =
            "parameterMap is a free-form {csvHeader: revitParameter} map; paramName1 is the placeholder",
        ["edit_family.paramName"] =
            "parameters is a free-form {name: value} map (any family parameter name); paramName is the placeholder",
    };

    /// <summary>
    /// Keys the SERVER consumes and never forwards: response shaping applied by
    /// ToolResponseShaper. Mirrors SERVER_SIDE_KEYS in tools/audit-tool-surface.py.
    /// </summary>
    private static readonly HashSet<string> ServerSideKeys = new(StringComparer.Ordinal)
    {
        "dryRun", "responseMode", "compact", "summaryOnly",
    };

    private static string RepoPath(params string[] parts)
    {
        var all = new List<string> { "..", "..", "..", ".." };
        all.AddRange(parts);
        return Path.GetFullPath(Path.Combine(all.ToArray()));
    }

    private static IEnumerable<string> CsFiles(params string[] parts)
    {
        var root = RepoPath(parts);
        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();
    }

    /// <summary>Candidate JSON keys announced in a description's brace groups.</summary>
    internal static IEnumerable<string> NestedKeys(string text)
    {
        foreach (Match group in BraceGroup.Matches(text ?? ""))
        {
            foreach (var raw in group.Groups[1].Value.Split(',', '|'))
            {
                var token = raw.Split(':')[0].Trim().Trim('?', '.', '[', ']', '*', ' ', '"');
                // camelCase identifiers of 3+ chars: short enough tokens are prose.
                if (token.Length < 3) continue;
                if (!Regex.IsMatch(token, @"^[a-z][A-Za-z0-9_]*$")) continue;
                if (Noise.Contains(token)) continue;
                yield return token;
            }
        }
    }

    [Fact]
    public void EveryNestedKeyAnnouncedInADescription_IsReadByTheRuntime()
    {
        // ── runtime side: every string literal, per tool, plus the shared corpus
        var literalsByFile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var fileByToolName = new Dictionary<string, string>(StringComparer.Ordinal);
        var corpus = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in CsFiles("RiveTT.Tools"))
        {
            var source = File.ReadAllText(file);
            var literals = new HashSet<string>(
                StringLiteral.Matches(source).Select(m => m.Groups[1].Value),
                StringComparer.Ordinal);

            // Newtonsoft binds a JSON key to a DTO property by name, case-insensitively,
            // so a property WorksetName reads the key worksetName without any literal.
            foreach (Match property in DtoProperty.Matches(source))
            {
                var name = property.Groups[1].Value;
                literals.Add(char.ToLowerInvariant(name[0]) + name.Substring(1));
            }

            literalsByFile[file] = literals;
            corpus.UnionWith(literals);

            foreach (Match match in RuntimeToolName.Matches(source))
                fileByToolName[match.Groups[1].Value] = file;
        }

        Assert.NotEmpty(fileByToolName);

        var offenders = new List<string>();

        foreach (var file in CsFiles("RiveTT.Server", "Tools"))
        {
            var source = File.ReadAllText(file);
            var sections = McpToolAttribute.Split(source);

            for (var i = 1; i < sections.Length - 1; i += 2)
            {
                var toolName = sections[i];
                var body = sections[i + 1];

                var targets = ExecuteTarget.Matches(body)
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (targets.Count == 0) targets.Add(toolName);

                var resolvedAny = false;
                var allowed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var target in targets)
                {
                    if (!fileByToolName.TryGetValue(target, out var targetFile)) continue;
                    resolvedAny = true;
                    allowed.UnionWith(literalsByFile[targetFile]);
                }
                if (!resolvedAny) continue;

                // Descriptions only: the section runs to the next attribute and therefore
                // includes the method body, whose C# blocks are not documentation.
                var described = string.Join(" ",
                    DescriptionText.Matches(body).Select(m => m.Groups[1].Value));

                foreach (var key in NestedKeys(described).Distinct(StringComparer.Ordinal))
                {
                    if (ServerSideKeys.Contains(key)) continue;
                    if (allowed.Contains(key)) continue;
                    // Read by a shared helper rather than the tool itself: still read.
                    if (corpus.Contains(key)) continue;
                    if (DocumentationOnly.ContainsKey($"{toolName}.{key}")) continue;

                    offenders.Add($"{toolName}: \"{key}\" is announced in the description but no "
                                + $"runtime source reads it (target: {string.Join("/", targets)})");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Nested keys announced to callers that nothing reads. A caller following the "
            + "documentation gets silence, not an error:\n  "
            + string.Join("\n  ", offenders.OrderBy(t => t, StringComparer.Ordinal)));
    }

    [Fact]
    public void CreateDimensions_AnnouncesElementIds_NotReferenceIds()
    {
        // The specific regression: referenceIds was documented for two releases and read
        // by nothing, so element-mode dimensions could not be created from the docs at all.
        var source = File.ReadAllText(RepoPath("RiveTT.Server", "Tools", "CreationTools.cs"));
        var start = source.IndexOf("Name = \"create_dimensions\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "create_dimensions wrapper not found");
        var end = source.IndexOf("[McpServerTool", start + 1, StringComparison.Ordinal);
        var section = end > start ? source.Substring(start, end - start) : source.Substring(start);

        Assert.DoesNotContain("referenceIds", section);
        Assert.Contains("elementIds", section);
    }

    [Fact]
    public void DocumentationOnlyWaivers_AllCarryAReason()
    {
        Assert.All(DocumentationOnly, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
    }

    [Fact]
    public void TheDetector_ActuallyExtractsKeysFromASpec()
    {
        // A contract test that silently stops finding keys would pass for ever. Pin the
        // extraction on the exact shape that shipped both defects.
        var keys = NestedKeys("[{number, name, titleBlockName?, viewIds?}]").ToList();

        Assert.Contains("viewIds", keys);
        Assert.Contains("titleBlockName", keys);
        // "number" and "name" are prose-grade nouns and stay filtered out.
        Assert.DoesNotContain("number", keys);
    }
}
