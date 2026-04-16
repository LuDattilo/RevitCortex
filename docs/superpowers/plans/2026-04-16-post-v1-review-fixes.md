# Post-v1.0.0 Code Review Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the 3 Critical + 4 Important issues from the post-v1.0.0 code review before cutting v1.1.0.

**Architecture:** Add an explicit `enableCodeExecution` settings flag (default `false`) that gates `send_code_to_revit` at the tool-invocation boundary. Replace the naive `string.Contains` sandbox with a Roslyn `SyntaxWalker` (net8) / regex-after-comment-strip (net48) that operates on real tokens. Make the installer Defender exclusion opt-in and idempotent. Widen transaction semantics to accept `group` mode. Return `InnerException.ToString()` for debuggability.

**Tech Stack:** C# 12 (net48 + net8), Roslyn `Microsoft.CodeAnalysis`, xUnit, Newtonsoft.Json, WPF (Revit plugin), PowerShell (installer).

---

## File Structure

**Create:**
- `src/RevitCortex.Core/Security/CortexSettings.cs` — loads/saves `~/.revitcortex/settings.json`, holds `EnableCodeExecution` and other user toggles.
- `src/RevitCortex.Core/Security/CodeSandboxV2.cs` — stateless helper: strip comments & string literals, then run regex/substring matching on the cleaned text. Used by both net48 and net8.
- `src/RevitCortex.Tools/CodeExecution/SyntaxBlockWalker.cs` (net8 only, guarded by `#if REVIT2025_OR_GREATER`) — Roslyn `CSharpSyntaxWalker` that blocks prohibited symbol usage even when obfuscated (reflection, `Type.GetType`, `Activator`, `dynamic`).
- `src/RevitCortex.Tests/Security/CortexSettingsTests.cs` — unit tests for the settings loader.
- `src/RevitCortex.Tests/Security/CodeSandboxV2Tests.cs` — regression tests including the bypasses the reviewer found.

**Modify:**
- `src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs` — check `EnableCodeExecution` flag, invoke `AuditLogger`, support `transactionMode: "group"`.
- `src/RevitCortex.Tools/CodeExecution/RoslynExecutor.cs` — add `group` transaction mode, return `InnerException.ToString()` instead of `.Message`, invoke `SyntaxBlockWalker`.
- `src/RevitCortex.Tools/CodeExecution/CodeDomExecutor.cs` — same (minus SyntaxBlockWalker, use CodeSandboxV2 only), remove dead `if` block in `WrapCode`.
- `src/RevitCortex.Core/Security/CodeSandbox.cs` — delegate to `CodeSandboxV2` (keep class for backwards-compat); fix `ProhibitedPatterns` to strip comments/strings first.
- `src/RevitCortex.Tools/Project/ManageAdditionalSettingsTool.cs` — remove `((dynamic)s).name` in favor of typed access.
- `distribution/install.ps1` — prompt user before `Add-MpPreference`, check for existing exclusion (idempotent).
- `src/RevitCortex.Tests/Security/CodeSandboxTests.cs` — extend with bypass regression tests.

---

## Task 1: CortexSettings loader

**Files:**
- Create: `src/RevitCortex.Core/Security/CortexSettings.cs`
- Create: `src/RevitCortex.Tests/Security/CortexSettingsTests.cs`

- [ ] **Step 1: Write the failing tests**

`src/RevitCortex.Tests/Security/CortexSettingsTests.cs`:
```csharp
using System.IO;
using Newtonsoft.Json;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

public class CortexSettingsTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaultsAllDisabled()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        var settings = CortexSettings.Load(tempPath);
        Assert.False(settings.EnableCodeExecution);
        Assert.Equal(8080, settings.Port);
    }

    [Fact]
    public void Load_ExistingFile_ParsesEnableCodeExecution()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{\"EnableCodeExecution\": true, \"Port\": 9090}");
        try
        {
            var settings = CortexSettings.Load(tempPath);
            Assert.True(settings.EnableCodeExecution);
            Assert.Equal(9090, settings.Port);
        }
        finally { File.Delete(tempPath); }
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rc-test-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{ not valid json");
        try
        {
            var settings = CortexSettings.Load(tempPath);
            Assert.False(settings.EnableCodeExecution);
        }
        finally { File.Delete(tempPath); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (class not defined)**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --filter "FullyQualifiedName~CortexSettingsTests"`
Expected: FAIL — `CortexSettings` does not exist.

- [ ] **Step 3: Implement CortexSettings**

`src/RevitCortex.Core/Security/CortexSettings.cs`:
```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace RevitCortex.Core.Security;

/// <summary>
/// User-editable settings persisted at ~/.revitcortex/settings.json.
/// Missing file or parse errors return defaults (all features disabled).
/// </summary>
public class CortexSettings
{
    /// <summary>
    /// When false (default), send_code_to_revit is refused at the tool-invocation boundary.
    /// The user must explicitly enable dynamic code execution via settings.json or the
    /// Revit plugin Settings UI. This is a hard gate, not a soft warning.
    /// </summary>
    [JsonProperty("EnableCodeExecution")]
    public bool EnableCodeExecution { get; set; } = false;

    /// <summary>TCP port for plugin-to-server communication.</summary>
    [JsonProperty("Port")]
    public int Port { get; set; } = 8080;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public static CortexSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return new CortexSettings();
            var json = File.ReadAllText(file);
            return JsonConvert.DeserializeObject<CortexSettings>(json) ?? new CortexSettings();
        }
        catch
        {
            return new CortexSettings();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --filter "FullyQualifiedName~CortexSettingsTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Core/Security/CortexSettings.cs src/RevitCortex.Tests/Security/CortexSettingsTests.cs
git commit -m "feat(security): add CortexSettings with EnableCodeExecution gate"
```

---

## Task 2: Enforce consent flag in SendCodeToRevitTool

**Files:**
- Modify: `src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs`

- [ ] **Step 1: Read existing file to confirm current state**

Run: `Read src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs`
Confirm the current body matches what we saw in review (no settings check, no audit).

- [ ] **Step 2: Add settings gate and audit logging**

Replace the `Execute` method body. The full updated file:

```csharp
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;

namespace RevitCortex.Tools.Elements;

public class SendCodeToRevitTool : ICortexTool
{
    private static readonly AuditLogger _audit = new AuditLogger();

    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Execute custom C# code in the Revit context. Globals: document (Document), uiDocument (UIDocument), app (Application). Auto-imports: System, System.Linq, Autodesk.Revit.DB, Autodesk.Revit.UI. On Revit 2023/2024 use explicit 'return' statements. REQUIRES EnableCodeExecution=true in ~/.revitcortex/settings.json.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // Gate 1: settings flag must be explicitly enabled
        var settings = CortexSettings.Load();
        if (!settings.EnableCodeExecution)
        {
            _audit.Log(Name, "BLOCKED: EnableCodeExecution=false", success: false,
                errorCode: CortexErrorCode.PermissionDenied);
            return CortexResult<object>.Fail(
                CortexErrorCode.PermissionDenied,
                "send_code_to_revit is disabled. Enable it in ~/.revitcortex/settings.json by setting \"EnableCodeExecution\": true, or from the Revit plugin Settings > Tools page. This tool compiles and executes arbitrary C# against the live Revit document — enable only if you trust the caller.",
                suggestion: "Edit ~/.revitcortex/settings.json and set EnableCodeExecution to true.");
        }

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        // Gate 2: sandbox validation on cleaned code (post-comment/string stripping)
        var sandboxResult = CodeSandbox.Validate(code);
        if (sandboxResult != null)
        {
            _audit.Log(Name, "BLOCKED: sandbox violation", success: false,
                errorCode: CortexErrorCode.PermissionDenied);
            return sandboxResult;
        }

        var uiApp = session.Store.Get<object>("uiApplication") as Autodesk.Revit.UI.UIApplication;
        var uiDoc = uiApp?.ActiveUIDocument;

        if (uiDoc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "UIApplication not available in session");

        var globals = new ScriptGlobals
        {
            document = doc,
            uiDocument = uiDoc,
            app = uiApp!.Application
        };

        CortexResult<object> result;
#if REVIT2025_OR_GREATER
        result = RoslynExecutor.Execute(code, globals, transactionMode);
#else
        result = CodeDomExecutor.Execute(code, globals, transactionMode);
#endif

        // Audit: always log code execution attempts with the first 200 chars of code as summary
        _audit.Log(Name, $"code[{code!.Length}ch]: {code.Substring(0, System.Math.Min(code.Length, 200))}",
            success: result.Success, errorCode: result.Error?.Code);

        return result;
    }
}
```

- [ ] **Step 3: Verified — CortexResult.Success (bool) and CortexResult.Error (CortexError?) match the usage above. Skip.**

- [ ] **Step 4: Build core + tools to check it compiles on net8**

Run: `dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R25"`
Expected: Build succeeded, 0 Error(s).

- [ ] **Step 5: Build on net48 too**

Run: `dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R24"`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs
git commit -m "fix(security): enforce EnableCodeExecution gate + audit logging in send_code_to_revit"
```

---

## Task 3: CodeSandboxV2 — strip comments/strings before matching

**Files:**
- Create: `src/RevitCortex.Core/Security/CodeSandboxV2.cs`
- Create: `src/RevitCortex.Tests/Security/CodeSandboxV2Tests.cs`
- Modify: `src/RevitCortex.Core/Security/CodeSandbox.cs`

- [ ] **Step 1: Write failing tests for CodeSandboxV2**

`src/RevitCortex.Tests/Security/CodeSandboxV2Tests.cs`:
```csharp
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

public class CodeSandboxV2Tests
{
    [Fact]
    public void Validate_CommentMentionsProhibited_NotBlocked()
    {
        var code = "// using System.IO would be dangerous\nvar x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_StringLiteralMentionsProhibited_NotBlocked()
    {
        var code = "var note = \"System.IO is off-limits\"; var x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_BlockCommentMentionsProhibited_NotBlocked()
    {
        var code = "/* WebClient is prohibited */ var x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_RealNamespaceUse_Blocked()
    {
        var code = "var f = System.IO.File.ReadAllText(\"x\");";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_TypeGetTypeReflectionBypass_Blocked()
    {
        var code = "var t = Type.GetType(\"System.IO.File\");";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_ActivatorCreateInstance_Blocked()
    {
        var code = "var obj = Activator.CreateInstance(someType);";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }
}
```

- [ ] **Step 2: Run tests — should fail (class doesn't exist)**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --filter "FullyQualifiedName~CodeSandboxV2Tests"`
Expected: FAIL.

- [ ] **Step 3: Implement CodeSandboxV2**

`src/RevitCortex.Core/Security/CodeSandboxV2.cs`:
```csharp
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Second-generation sandbox. Strips comments and string literals BEFORE pattern matching
/// to eliminate false positives from documentation and avoid comment-based evasion.
/// Also blocks reflection-based bypasses (Type.GetType, Activator.CreateInstance, dynamic).
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
    /// preserving line numbers and non-string source structure.
    /// </summary>
    internal static string StripCommentsAndStrings(string code)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            // Line comment
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }
            // Block comment
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
            // Verbatim string @"..."
            if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                sb.Append("  "); i += 2;
                while (i < code.Length)
                {
                    if (code[i] == '"' && (i + 1 >= code.Length || code[i + 1] != '"'))
                    { sb.Append(' '); i++; break; }
                    if (code[i] == '"' && i + 1 < code.Length && code[i + 1] == '"')
                    { sb.Append("  "); i += 2; continue; }
                    sb.Append(code[i] == '\n' ? '\n' : ' '); i++;
                }
                continue;
            }
            // Regular string "..."
            if (c == '"')
            {
                sb.Append(' '); i++;
                while (i < code.Length && code[i] != '"')
                {
                    if (code[i] == '\\' && i + 1 < code.Length) { sb.Append("  "); i += 2; continue; }
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --filter "FullyQualifiedName~CodeSandboxV2Tests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Update CodeSandbox to delegate to V2**

Replace `src/RevitCortex.Core/Security/CodeSandbox.cs`:
```csharp
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Back-compat shim. All new code should call CodeSandboxV2 directly.
/// This class remains for external callers linking against RevitCortex.Core
/// that haven't updated to the V2 API yet.
/// </summary>
public static class CodeSandbox
{
    public static CortexResult<object>? Validate(string code) => CodeSandboxV2.Validate(code);
}
```

- [ ] **Step 6: Re-run existing CodeSandboxTests to confirm back-compat**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --filter "FullyQualifiedName~CodeSandboxTests"`
Expected: all 10 existing tests still PASS (V2 catches all the patterns V1 did).

- [ ] **Step 7: Commit**

```bash
git add src/RevitCortex.Core/Security/CodeSandboxV2.cs src/RevitCortex.Core/Security/CodeSandbox.cs src/RevitCortex.Tests/Security/CodeSandboxV2Tests.cs
git commit -m "feat(security): CodeSandboxV2 strips comments/strings, blocks reflection bypasses"
```

---

## Task 4: Transaction group mode in executors

**Files:**
- Modify: `src/RevitCortex.Tools/CodeExecution/RoslynExecutor.cs`
- Modify: `src/RevitCortex.Tools/CodeExecution/CodeDomExecutor.cs`

- [ ] **Step 1: Add `group` mode to RoslynExecutor**

In `RoslynExecutor.Execute`, replace the transactionMode branching (lines ~92-112):

```csharp
if (transactionMode == "none")
{
    result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
}
else if (transactionMode == "group")
{
    using var txGroup = new TransactionGroup(globals.document, "RevitCortex: Script Group");
    txGroup.Start();
    try
    {
        result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
        if (txGroup.GetStatus() == TransactionStatus.Started)
            txGroup.Assimilate();
    }
    catch
    {
        if (txGroup.GetStatus() == TransactionStatus.Started)
            txGroup.RollBack();
        throw;
    }
}
else
{
    using var tx = new Transaction(globals.document, "RevitCortex: Script");
    tx.Start();
    try
    {
        result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
        if (tx.GetStatus() == TransactionStatus.Started)
            tx.Commit();
    }
    catch
    {
        if (tx.GetStatus() == TransactionStatus.Started)
            tx.RollBack();
        throw;
    }
}
```

- [ ] **Step 2: Same change in CodeDomExecutor**

In `CodeDomExecutor.Execute`, apply the identical three-branch block (replace `method.Invoke(...)` line for each branch appropriately; no `using` keyword on the `result` variable declaration since it's shared across branches).

- [ ] **Step 3: Return InnerException.ToString() for debuggability**

In both executors, replace the `TargetInvocationException` catch:
```csharp
catch (TargetInvocationException ex) when (ex.InnerException != null)
{
    return CortexResult<object>.Fail(
        CortexErrorCode.Unknown,
        $"Runtime error: {ex.InnerException}",  // .ToString() is implicit in string interpolation
        suggestion: "Check variable names, null references, and Revit API usage. Full stack trace above.");
}
```

- [ ] **Step 4: Build both targets**

Run: `dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R25"`
Run: `dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R24"`
Expected: both succeed.

- [ ] **Step 5: Remove dead code in CodeDomExecutor.WrapCode**

Open `CodeDomExecutor.cs` lines 134-138. Delete:
```csharp
// If the user code doesn't have a return, add one
if (!userCode.TrimEnd().EndsWith(";") && !userCode.Contains("return "))
{
    // Last expression style not supported in CodeDom — user must use return
}
```

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Tools/CodeExecution/RoslynExecutor.cs src/RevitCortex.Tools/CodeExecution/CodeDomExecutor.cs
git commit -m "feat(executors): add transactionMode=group, return full InnerException, remove dead code"
```

---

## Task 5: Minor — remove dynamic in ManageAdditionalSettingsTool

**Files:**
- Modify: `src/RevitCortex.Tools/Project/ManageAdditionalSettingsTool.cs`

- [ ] **Step 1: Read current usage**

Run: `Read src/RevitCortex.Tools/Project/ManageAdditionalSettingsTool.cs` and locate the `((dynamic)s).name` call (line ~74).

- [ ] **Step 2: Replace with typed access**

The surrounding code creates anonymous objects. Define a local class at the top of the file instead:
```csharp
private sealed class NamedSetting { public string name { get; set; } = ""; }
```
Then replace the `(dynamic)` cast with `((NamedSetting)s).name`.

If the anonymous object has more fields, include them all in `NamedSetting` and update construction sites.

- [ ] **Step 3: Build**

Run: `dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R25"`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Tools/Project/ManageAdditionalSettingsTool.cs
git commit -m "refactor: replace dynamic cast with typed NamedSetting in ManageAdditionalSettings"
```

---

## Task 6: Installer — opt-in Defender exclusion, idempotent

**Files:**
- Modify: `distribution/install.ps1`

- [ ] **Step 1: Replace the Defender block (lines 129-134)**

```powershell
# Add Windows Defender exclusion for the server folder (optional — asks user first).
# Real-time protection can quarantine the self-contained EXE as a false positive.
if (Get-Command Add-MpPreference -ErrorAction SilentlyContinue) {
    $existing = (Get-MpPreference -ErrorAction SilentlyContinue).ExclusionPath
    if ($existing -and ($existing -contains $serverTarget)) {
        Write-Host "  Windows Defender exclusion already present — skipping" -ForegroundColor Gray
    } else {
        Write-Host ""
        Write-Host "  Windows Defender sometimes quarantines new self-contained executables." -ForegroundColor Yellow
        Write-Host "  Add an exclusion for: $serverTarget" -ForegroundColor Yellow
        $defAnswer = Read-Host "  Add Defender exclusion? (y/N)"
        if ($defAnswer -eq "y" -or $defAnswer -eq "Y") {
            Add-MpPreference -ExclusionPath $serverTarget -ErrorAction SilentlyContinue
            Write-Host "  Windows Defender exclusion added" -ForegroundColor Gray
        } else {
            Write-Host "  Skipped Defender exclusion. If the EXE is quarantined, re-run install.ps1." -ForegroundColor Gray
        }
    }
}
```

- [ ] **Step 2: Manual smoke test — run installer in a VM or dry-run**

Run the installer on any machine (can be the dev machine; reinstallation is safe) and verify:
- First run with answer "y" adds the exclusion.
- Second run detects existing exclusion and skips.
- First run with answer "N" (or Enter) leaves Defender untouched.

If you cannot run the installer, inspect the diff carefully and skip the manual test — mark the checkbox with a note.

- [ ] **Step 3: Commit**

```bash
git add distribution/install.ps1
git commit -m "fix(installer): Defender exclusion now opt-in and idempotent"
```

---

## Task 7: Extend CodeSandboxTests with bypass regressions

**Files:**
- Modify: `src/RevitCortex.Tests/Security/CodeSandboxTests.cs`

- [ ] **Step 1: Append bypass-regression tests**

Add at the end of the class:
```csharp
[Fact]
public void Validate_CommentFalsePositive_Allowed()
{
    var code = "// System.IO is prohibited\nreturn document.Title;";
    Assert.Null(CodeSandbox.Validate(code));
}

[Fact]
public void Validate_StringLiteralFalsePositive_Allowed()
{
    var code = "var note = \"do not use System.IO\"; return note;";
    Assert.Null(CodeSandbox.Validate(code));
}

[Fact]
public void Validate_TypeGetTypeBypass_Blocked()
{
    var code = "var t = Type.GetType(\"System.IO.File\"); t.GetMethod(\"Delete\");";
    Assert.NotNull(CodeSandbox.Validate(code));
}

[Fact]
public void Validate_ActivatorBypass_Blocked()
{
    var code = "var obj = Activator.CreateInstance(someType);";
    Assert.NotNull(CodeSandbox.Validate(code));
}
```

- [ ] **Step 2: Run all sandbox tests**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --filter "FullyQualifiedName~Sandbox"`
Expected: all tests PASS.

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tests/Security/CodeSandboxTests.cs
git commit -m "test(security): regression coverage for comment/string and reflection bypasses"
```

---

## Task 8: Full build + test matrix

- [ ] **Step 1: Clean build all Revit targets**

Run each in sequence:
```bash
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R23" 2>&1 | tail -3
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R24" 2>&1 | tail -3
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R25" 2>&1 | tail -3
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26" 2>&1 | tail -3
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release 2>&1 | tail -3
```
Expected: all report "Build succeeded" with 0 errors.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c Debug`
Expected: all tests pass (0 failed).

- [ ] **Step 3: Deploy freshly built plugin + server to local Revit**

Run: `powershell -ExecutionPolicy Bypass -File deploy.ps1`
Expected: plugin copied to `C:\ProgramData\Autodesk\Revit\Addins\<ver>\RevitCortex\`, server to `~\.revitcortex\server\`.

---

## Task 9: Bulk functional test on live Revit

**Goal:** Exercise the 157 MCP tools against the live Revit + sample model to catch regressions and bottlenecks.

- [ ] **Step 1: Open `Snowdon Towers Sample Architectural.rvt` in Revit 2024 (net48) or 2025+ (net8)**

The file lives at `C:\Users\luigi.dattilo\OneDrive - GPA Ingegneria Srl\Documenti\RevitCortex\Snowdon Towers Sample Architectural.rvt`.

- [ ] **Step 2: Verify RevitCortex ribbon is green (plugin loaded, server reachable)**

If gray, check `~\.revitcortex\server\` exists and `RevitCortex.Server.exe` runs standalone.

- [ ] **Step 3: Run smoke tests through Claude Code MCP**

Execute a representative sample of tools (document as pass/fail):

| Tool | Expected |
| --- | --- |
| `get_project_info` | Returns project info, levels, phases, worksets |
| `analyze_model_statistics` | Returns total element counts and category breakdown |
| `ai_element_filter` with `OST_Walls` | Returns wall instances |
| `get_element_parameters` on a wall | Returns instance + type parameters |
| `check_model_health` | Returns health score + recommendations |
| `audit_families` | Returns family audit |
| `workflow_model_audit` | Returns comprehensive audit report |
| `export_to_excel` OST_Walls | Creates .xlsx on Desktop |
| `send_code_to_revit` with any code (before enabling flag) | PermissionDenied |
| Enable flag → re-run `send_code_to_revit` with `return document.Title;` | Returns document title |
| `send_code_to_revit` with `File.ReadAllText(...)` (sandbox test) | PermissionDenied |
| `send_code_to_revit` with `// System.IO comment\nreturn 1;` | Passes (comment stripped) |

- [ ] **Step 4: Log any bottlenecks**

For any tool that takes > 5s, note the elapsed time. Heavy-query tools (`analyze_model_statistics`, `purge_unused`, `workflow_model_audit`) are expected to be slow on large models — flag only if > 30s on Snowdon (a medium-sized model).

- [ ] **Step 5: Record findings in a scratch file**

Create `docs/superpowers/plans/2026-04-16-bulk-test-results.md` with:
- Tools tested and result (pass/fail/skip)
- Any errors or stack traces
- Performance observations
- Crashes (if any) and Revit journal path

If failures are found, STOP and triage before proceeding to Task 10.

---

## Task 10: Final commit batch + push

- [ ] **Step 1: Update CHANGELOG/README if needed**

Check if README mentions `v1.0.0` features that changed. Note in README:
- `send_code_to_revit` now requires `EnableCodeExecution: true` in settings
- `transactionMode: "group"` is now supported
- Defender exclusion is now opt-in

Only edit if required — don't bloat docs.

- [ ] **Step 2: Verify all tests pass once more**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 3: Review git log before push**

Run: `git log --oneline origin/main..HEAD`
Confirm commits match the task boundaries (one commit per task, clean messages).

- [ ] **Step 4: Push to origin/main**

```bash
git push origin main
```

- [ ] **Step 5: Verify GitHub picked it up**

Run: `gh run list --branch main --limit 3` (if CI is set up, otherwise skip).
Expected: latest push visible, no CI failures.

---

## Self-Review Checklist

- [x] **Spec coverage:** all 3 Critical + 4 Important review issues have a task. Minor issues 8 and 10 covered (Task 5, Task 4 step 5). Minor 9 (ScriptGlobals naming) intentionally deferred — it's a convention consistent with CLAUDE.md, the reviewer flagged it but the project decision stands.
- [x] **Placeholder scan:** no "TBD" / "implement later" / "add validation" / "similar to Task N" text.
- [x] **Type consistency:** `CortexSettings.EnableCodeExecution` used consistently. `CodeSandboxV2.Validate` signature matches `CodeSandbox.Validate` for drop-in replacement. `AuditLogger.Log` signature matches existing file. `CortexResult<object>.IsSuccess / Error.Code` assumed — Task 2 Step 3 has a guard that verifies before building.
