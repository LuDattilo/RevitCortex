using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Validates user-submitted C# code before execution.
/// Blocks access to dangerous namespace patterns to prevent
/// prompt injection attacks via send_code_to_revit.
/// </summary>
public static class CodeSandbox
{
    /// <summary>
    /// Namespace patterns that are prohibited in user-submitted code.
    /// Each entry is checked as a substring match against the code text.
    /// </summary>
    private static readonly string[] ProhibitedPatterns = new[]
    {
        "System.IO",
        "System.Net",
        "System.Diagnostics.Process",
        "Microsoft.Win32",
        "System.Reflection.Emit",
        "System.Runtime.InteropServices",
    };

    /// <summary>
    /// Regex patterns for more subtle evasion attempts (e.g., using aliases).
    /// </summary>
    private static readonly Regex[] ProhibitedRegexes = new[]
    {
        // Catch Process.Start even without full namespace
        new Regex(@"\bProcess\s*\.\s*Start\b", RegexOptions.Compiled),
        // Catch File/Directory/Path static method calls (prefix match for ReadAllText, WriteAllLines, etc.)
        new Regex(@"\b(File|Directory|Path)\s*\.\s*(Read|Write|Delete|Move|Copy|Create|Exists|Open|Append|GetFiles|GetDirectories)\w*", RegexOptions.Compiled),
        // Catch WebClient, HttpClient usage
        new Regex(@"\b(WebClient|HttpClient|WebRequest|HttpWebRequest|TcpClient|Socket)\b", RegexOptions.Compiled),
        // Catch Registry access
        new Regex(@"\bRegistry(Key)?\s*\.\s*(Open|Get|Set|Create|Delete)\b", RegexOptions.Compiled),
        // Catch Environment.Exit or unsafe environment manipulation
        new Regex(@"\bEnvironment\s*\.\s*(Exit|SetEnvironmentVariable)\b", RegexOptions.Compiled),
        // Catch Assembly.Load for dynamic loading
        new Regex(@"\bAssembly\s*\.\s*(Load|LoadFrom|LoadFile)\b", RegexOptions.Compiled),
    };

    /// <summary>
    /// Validates code against the sandbox rules.
    /// Returns null if the code is safe, or a CortexResult with PermissionDenied if violations found.
    /// </summary>
    public static CortexResult<object>? Validate(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null; // Empty code is handled elsewhere

        var violations = new List<string>();

        // Check namespace patterns
        foreach (var pattern in ProhibitedPatterns)
        {
            if (code.Contains(pattern))
                violations.Add(pattern);
        }

        // Check regex patterns for evasion attempts
        foreach (var regex in ProhibitedRegexes)
        {
            var match = regex.Match(code);
            if (match.Success)
                violations.Add(match.Value);
        }

        if (violations.Count == 0)
            return null;

        return CortexResult<object>.Fail(
            CortexErrorCode.PermissionDenied,
            $"Code contains prohibited operations: {string.Join(", ", violations)}",
            suggestion: "send_code_to_revit is restricted to Revit API operations only. "
                + "File I/O, network access, process spawning, and registry access are not allowed.");
    }
}
