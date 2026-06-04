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
        "System.Reflection",   // covers .Emit too; blocks reflection-based sandbox escapes
        "System.Runtime.InteropServices",
    };

    // Patterns checked against the comments/strings-stripped form of the code.
    // String literals collapse to whitespace here, which is what we want for namespace
    // and identifier matching but defeats us when we need to know whether a call has
    // an argument — see ReflectionWithArgumentPatterns below for those cases.
    private static readonly Regex[] ProhibitedPatterns = new[]
    {
        new Regex(@"\bProcess\s*\.\s*Start\b", RegexOptions.Compiled),
        new Regex(@"\b(File|Directory|Path)\s*\.\s*(Read|Write|Delete|Move|Copy|Create|Exists|Open|Append|GetFiles|GetDirectories)\w*", RegexOptions.Compiled),
        new Regex(@"\b(WebClient|HttpClient|WebRequest|HttpWebRequest|TcpClient|Socket)\b", RegexOptions.Compiled),
        new Regex(@"\bRegistry(Key)?\s*\.\s*(Open|Get|Set|Create|Delete)\b", RegexOptions.Compiled),
        new Regex(@"\bEnvironment\s*\.\s*(Exit|SetEnvironmentVariable|GetEnvironmentVariable|GetEnvironmentVariables|GetFolderPath|GetCommandLineArgs|ExpandEnvironmentVariables|MachineName|UserName|UserDomainName|CurrentDirectory|SystemDirectory|ProcessPath|StackTrace)\b", RegexOptions.Compiled),
        new Regex(@"\bAssembly\s*\.\s*(Load|LoadFrom|LoadFile)\b", RegexOptions.Compiled),
        // Reflection bypasses (no-arg / fixed-form)
        new Regex(@"\bType\s*\.\s*GetType\b", RegexOptions.Compiled),
        new Regex(@"\bActivator\s*\.\s*CreateInstance\b", RegexOptions.Compiled),
        new Regex(@"\bMethodInfo\s*\.\s*Invoke\b", RegexOptions.Compiled),
        // typeof(X).<reflection-accessor> — the typeof() makes Type.GetType unnecessary
        new Regex(@"\btypeof\s*\([^)]+\)\s*\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\b", RegexOptions.Compiled),
        // dynamic keyword opens late-bound dispatch — bypasses static pattern matching entirely
        new Regex(@"\bdynamic\b", RegexOptions.Compiled),
        // Reflection enumerators (zero-arg, plural) — the entry point for "enumerate members then
        // Invoke one" escapes. Distinct from the deliberately-allowed singular obj.GetType() and
        // type.GetMethod("name") (the latter is caught by ReflectionWithArgumentPatterns when abused).
        new Regex(@"\.\s*(GetTypes|GetMethods|GetConstructors|GetMembers|GetFields|GetProperties|GetInterfaces|GetNestedTypes|GetRuntimeMethods|GetRuntimeFields|GetRuntimeProperties)\s*\(", RegexOptions.Compiled),
        // Any reflective invoke on a value (m.Invoke(...), ctor.Invoke(...), del.DynamicInvoke(...)).
        // The earlier \bMethodInfo\.Invoke\b literal only caught the class-name form, not a variable.
        new Regex(@"\.\s*(Invoke|DynamicInvoke)\s*\(", RegexOptions.Compiled),
        // Assembly acquisition — the root of a reflection walk over loaded types.
        new Regex(@"\b(GetExecutingAssembly|GetCallingAssembly|GetEntryAssembly)\b", RegexOptions.Compiled),
    };

    // Patterns checked against the ORIGINAL code (string literals NOT stripped). We need this
    // to distinguish `obj.GetType()` (harmless zero-arg type check) from `obj.GetType("ns.T")`
    // (reflection bypass via Assembly.GetType / typeof(X).Module.GetType / instance.GetType).
    // After stripping, both look like `obj.GetType(...whitespace...)` and we can't tell them apart.
    private static readonly Regex[] ReflectionWithArgumentPatterns = new[]
    {
        // .GetType( <something non-whitespace> ) — covers Assembly.GetType("..."), .GetType(name),
        // .GetType(ns + "." + cls). Excludes .GetType() (zero-arg, harmless).
        new Regex(@"\.\s*GetType\s*\(\s*\S[^)]*\)", RegexOptions.Compiled),
        // .GetMethod( <something non-whitespace> ) etc. — same rationale; excludes zero-arg overloads.
        new Regex(@"\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\s*\(\s*\S[^)]*\)", RegexOptions.Compiled),
    };

    public static CortexResult<object>? Validate(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var cleaned = StripCommentsAndStrings(code);
        // Collapse whitespace around member-access dots so a prohibited namespace cannot be
        // smuggled past the substring check as "System . IO" or "System.\n  IO" (C2 hardening).
        var normalized = Regex.Replace(cleaned, @"\s*\.\s*", ".");
        var violations = new List<string>();

        foreach (var ns in ProhibitedNamespaces)
        {
            if (normalized.Contains(ns))
                violations.Add(ns);
        }

        foreach (var regex in ProhibitedPatterns)
        {
            var match = regex.Match(cleaned);
            if (match.Success)
                violations.Add(match.Value);
        }

        // Reflection-with-argument patterns must match the ORIGINAL code so we can tell
        // `obj.GetType()` (allowed) from `obj.GetType("System.IO.File")` (blocked).
        foreach (var regex in ReflectionWithArgumentPatterns)
        {
            var match = regex.Match(code);
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
