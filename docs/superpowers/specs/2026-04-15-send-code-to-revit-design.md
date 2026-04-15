# send_code_to_revit — Roslyn Scripting Engine

## Goal

Enable execution of arbitrary C# code inside the Revit process via the `send_code_to_revit` MCP tool, using Roslyn Scripting API. This unlocks bulk operations, custom queries, and any Revit API operation not covered by dedicated tools.

## Architecture

```
Claude → send_code_to_revit tool call
  → SendCodeToRevitTool.Execute()
    → CodeSandbox.Validate(code)          [existing, blocks dangerous patterns]
    → RoslynExecutor.ExecuteAsync(code, globals, transactionMode)
      → CSharpScript.EvaluateAsync(code, options, globals)
      → Serialize result to JSON
    → Return CortexResult<object>
```

## Components

### 1. ScriptGlobals (new: RevitCortex.Plugin/CodeExecution/ScriptGlobals.cs)

Simple POCO class exposing pre-injected variables to the script context:

```csharp
public class ScriptGlobals
{
    public Document document { get; set; }
    public UIDocument uiDocument { get; set; }
    public Application app { get; set; }
}
```

Lowercase property names match the documented convention in CLAUDE.md (`document`, not `Doc`).

### 2. RoslynExecutor (new: RevitCortex.Plugin/CodeExecution/RoslynExecutor.cs)

Responsibilities:
- Configure Roslyn ScriptOptions with auto-imports and assembly references
- Compile and execute code via `CSharpScript.EvaluateAsync<object>()`
- Handle Transaction wrapping when `transactionMode == "auto"`
- Enforce timeout (30 seconds)
- Serialize the return value to a JSON-friendly object
- Catch and format compilation errors and runtime exceptions

Auto-imports injected into every script:
```
System
System.Linq
System.Collections.Generic
Autodesk.Revit.DB
Autodesk.Revit.UI
```

Assembly references: all assemblies currently loaded in the AppDomain (covers Revit API, Newtonsoft.Json, etc.)

Transaction logic for `transactionMode == "auto"`:
- Wrap execution in `using (var tx = new Transaction(document, "RevitCortex: Script")) { tx.Start(); ... tx.Commit(); }`
- If the script throws, `tx.RollBack()` automatically

For `transactionMode == "none"`:
- Execute without transaction wrapping (read-only queries)

### 3. SendCodeToRevitTool (modify existing)

Replace the "not yet implemented" error with actual execution:
- Call `CodeSandbox.Validate()` (already there)
- Instantiate `ScriptGlobals` from session
- Call `RoslynExecutor.ExecuteAsync()`
- Return result

On net48 builds (R23/R24), return:
```
CortexResult.Fail(CortexErrorCode.InvalidInput, 
    "send_code_to_revit requires Revit 2025+ (.NET 8)")
```

### 4. NuGet Package

Add `Microsoft.CodeAnalysis.CSharp.Scripting` to `RevitCortex.Plugin.csproj`:
- Only for net8+ target framework (R25/R26)
- Use `Condition="'$(TargetFramework)' != 'net48'"` in the csproj
- Wrap Roslyn code in `#if REVIT2025_OR_GREATER` directives

## Execution Model

**Script style (option A):** User writes "flat" code, last expression is the return value.

```csharp
// User input:
var walls = new FilteredElementCollector(document)
    .OfClass(typeof(Wall)).Cast<Wall>().ToList();
walls.Select(w => new { w.Id, w.Name }).Take(5)

// Returns: [{"Id": 619340, "Name": "Exterior..."}, ...]
```

`return` also works for explicit returns.

## Security

Handled by existing `CodeSandbox.Validate()` — no changes needed:
- Blocks: System.IO, System.Net, Process, Registry, Reflection.Emit, InteropServices
- Regex patterns catch evasion attempts (File.Read*, HttpClient, etc.)
- 67 existing unit tests verify the sandbox

## Result Serialization

The script return value is serialized to JSON via Newtonsoft.Json:
- Primitives (int, string, bool, double) → direct JSON values
- Anonymous objects → JSON objects
- Collections → JSON arrays
- Revit Element objects → serialized as `{ elementId, name, category }` summary
- null → `{ "result": null }`
- Compilation error → `CortexResult.Fail(InvalidInput, "Compilation error: ...")`
- Runtime exception → `CortexResult.Fail(Unknown, "Runtime error: ...")`

## Timeout

Default 30 seconds. Enforced via `CancellationTokenSource` passed to `EvaluateAsync`. On timeout:
```
CortexResult.Fail(CortexErrorCode.Timeout, "Script execution timed out after 30s")
```

## Revit Version Support

| Revit | .NET | send_code_to_revit |
|-------|------|--------------------|
| 2023 | net48 | Returns error: "requires Revit 2025+" |
| 2024 | net48 | Returns error: "requires Revit 2025+" |
| 2025 | net8.0 | Full Roslyn support |
| 2026 | net8.0 | Full Roslyn support |

## Files

| Action | File |
|--------|------|
| Create | `src/RevitCortex.Plugin/CodeExecution/ScriptGlobals.cs` |
| Create | `src/RevitCortex.Plugin/CodeExecution/RoslynExecutor.cs` |
| Modify | `src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs` |
| Modify | `src/RevitCortex.Plugin/RevitCortex.Plugin.csproj` (add Roslyn NuGet) |
| Create | `src/RevitCortex.Tests/CodeExecution/RoslynExecutorTests.cs` (basic tests) |
