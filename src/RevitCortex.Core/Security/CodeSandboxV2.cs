using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Second-generation sandbox. Strips comments and string literals BEFORE pattern matching
/// to eliminate false positives from documentation and avoid comment-based evasion.
/// Also blocks reflection-based bypasses (Type.GetType, Activator.CreateInstance,
/// MethodInfo.Invoke) that the substring-only V1 missed.
/// </summary>
public static class CodeSandboxV2
{
    private static readonly string[] ProhibitedNamespaces = new[]
    {
        "System.IO",
        "System.Net",
        "System.Diagnostics.Process",
        "Microsoft.Win32",
        "System.Reflection.Emit",
        "System.Runtime.InteropServices",
    };

    private static readonly Regex[] ProhibitedPatterns = new[]
    {
        new Regex(@"\bProcess\s*\.\s*Start\b", RegexOptions.Compiled),
        new Regex(@"\b(File|Directory|Path)\s*\.\s*(Read|Write|Delete|Move|Copy|Create|Exists|Open|Append|GetFiles|GetDirectories)\w*", RegexOptions.Compiled),
        new Regex(@"\b(WebClient|HttpClient|WebRequest|HttpWebRequest|TcpClient|Socket)\b", RegexOptions.Compiled),
        new Regex(@"\bRegistry(Key)?\s*\.\s*(Open|Get|Set|Create|Delete)\b", RegexOptions.Compiled),
        new Regex(@"\bEnvironment\s*\.\s*(Exit|SetEnvironmentVariable)\b", RegexOptions.Compiled),
        new Regex(@"\bAssembly\s*\.\s*(Load|LoadFrom|LoadFile)\b", RegexOptions.Compiled),
        // Reflection bypasses
        new Regex(@"\bType\s*\.\s*GetType\b", RegexOptions.Compiled),
        new Regex(@"\bActivator\s*\.\s*CreateInstance\b", RegexOptions.Compiled),
        new Regex(@"\bMethodInfo\s*\.\s*Invoke\b", RegexOptions.Compiled),
        // Reflection bypasses round 2 (discovered 2026-05-15 bypass exploration)
        // Any .GetType(<non-empty>) on a non-Type identifier — covers Assembly.GetType("..."),
        // typeof(X).Module.GetType(...), instance.GetType(...). Strings are stripped to spaces
        // before matching, so we look for a non-empty argument (anything except whitespace-only).
        new Regex(@"\.\s*GetType\s*\(\s*\S", RegexOptions.Compiled),
        // typeof(X).GetMethod / GetField / GetProperty / InvokeMember — bypasses Type.GetType guard
        new Regex(@"\btypeof\s*\([^)]+\)\s*\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\b", RegexOptions.Compiled),
        // Direct GetMethod / GetField / GetProperty access on any expression (covers `someType.GetMethod(...)`)
        // String literals are stripped to spaces before matching, so we look for any non-empty arg.
        new Regex(@"\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\s*\(\s*\S", RegexOptions.Compiled),
        // dynamic keyword opens late-bound dispatch — bypasses static pattern matching entirely
        new Regex(@"\bdynamic\b", RegexOptions.Compiled),
    };

    public static CortexResult<object>? Validate(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var cleaned = StripCommentsAndStrings(code);
        var violations = new List<string>();

        foreach (var ns in ProhibitedNamespaces)
        {
            if (cleaned.Contains(ns))
                violations.Add(ns);
        }

        foreach (var regex in ProhibitedPatterns)
        {
            var match = regex.Match(cleaned);
            if (match.Success)
                violations.Add(match.Value);
        }

        if (violations.Count == 0) return null;

        return CortexResult<object>.Fail(
            CortexErrorCode.PermissionDenied,
            $"Code contains prohibited operations: {string.Join(", ", violations)}",
            suggestion: "send_code_to_revit is restricted to Revit API operations. "
                + "File I/O, network, process spawning, registry, and reflection bypasses are not allowed.");
    }

    /// <summary>
    /// Replace every comment and string literal with whitespace of the same length,
    /// preserving line numbers and non-string source structure. Not a full C# lexer —
    /// good enough to defeat comment/string-based evasion of the pattern matcher.
    /// </summary>
    public static string StripCommentsAndStrings(string code)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];

            // Line comment //...
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }

            // Block comment /* ... */
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                sb.Append("  ");
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/'))
                {
                    sb.Append(code[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i + 1 < code.Length) { sb.Append("  "); i += 2; }
                continue;
            }

            // Verbatim string @"..."  (escapes are "")
            if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                sb.Append("  "); i += 2;
                while (i < code.Length)
                {
                    if (code[i] == '"')
                    {
                        if (i + 1 < code.Length && code[i + 1] == '"')
                        {
                            sb.Append("  "); i += 2; continue; // escaped quote
                        }
                        sb.Append(' '); i++; break;
                    }
                    sb.Append(code[i] == '\n' ? '\n' : ' '); i++;
                }
                continue;
            }

            // Regular string "..."  (backslash escapes)
            if (c == '"')
            {
                sb.Append(' '); i++;
                while (i < code.Length && code[i] != '"')
                {
                    if (code[i] == '\\' && i + 1 < code.Length)
                    {
                        sb.Append("  "); i += 2; continue;
                    }
                    sb.Append(code[i] == '\n' ? '\n' : ' '); i++;
                }
                if (i < code.Length) { sb.Append(' '); i++; }
                continue;
            }

            // Char literal '.'
            if (c == '\'')
            {
                sb.Append(' '); i++;
                while (i < code.Length && code[i] != '\'')
                {
                    if (code[i] == '\\' && i + 1 < code.Length) { sb.Append("  "); i += 2; continue; }
                    sb.Append(' '); i++;
                }
                if (i < code.Length) { sb.Append(' '); i++; }
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
