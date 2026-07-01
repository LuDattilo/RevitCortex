# Dynamo Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `RevitCortex.Tools.Dynamo` project exposing 4 MCP tools (`dynamo_get_status`, `dynamo_list_graph_io`, `dynamo_generate_graph`, `dynamo_run_graph`) that generate valid Python-centric `.dyn` graphs deterministically and (optionally) run them headless inside Revit — an escape-hatch to the full Revit API.

**Architecture:** New isolated project referenced by the plugin. Graph generation is deterministic and never loads Dynamo DLLs (3 of 4 tools never touch Dynamo). Only `dynamo_run_graph` late-binds Dynamo via reflection, so a missing/incompatible Dynamo install degrades gracefully instead of breaking the plugin. Gated by a new `EnableDynamo` setting (default false), a `PythonSandbox` (reusing `CodeSandboxV2`), and per-op confirmation — mirroring `send_code_to_revit`.

**Tech Stack:** C# multi-target (net48 / net8.0-windows / net10.0-windows), xUnit, Newtonsoft.Json, reflection-based late-binding for Dynamo runtime. Spec: `docs/superpowers/specs/2026-07-01-dynamo-integration-design.md`.

**Cross-target rule (from CLAUDE.md):** no `record`/`init`/`Index`/`Range`/`GetValueOrDefault()`/default-interface-methods (break on net48). Build BOTH `Debug R25` (net8) AND `Debug R24` (net48) before committing any C# change. The new tools live in a THIRD assembly — a green Plugin build can mask a Tools.Dynamo compile error, so build the Tools.Dynamo project explicitly (memory: "Plugin build masks Tools compile errors").

**Verified `.dyn` schema facts (from DynamoDS/Dynamo master):**
- PythonScriptNode: `ConcreteType = "PythonNodeModels.PythonNode, PythonNodeModels"`, `NodeType = "PythonScriptNode"`, engine in JSON field `"Engine"` with exact value `"CPython3"`, `VariableInputPorts: true`, `Replication: "Disabled"`. Code in `Code` (newlines `\r\n`); output assigned to `OUT`, inputs arrive as list `IN`.
- Connector has NO `ConcreteType` — only `{ Start, End, Id, IsHidden }`. `IsHidden` is the STRING `"False"`/`"True"`. `Start` = source **output-port** Id, `End` = destination **input-port** Id (port Ids, not node Ids).
- Port (identical in Inputs and Outputs): `{ Id, Name, Description, UsingDefaultValue, Level, UseLevels, KeepListStructure }`. Port has NO `NodeType`.
- Every node needs a `View.NodeViews[]` entry with matching `Id`: `{ Id, Name, IsSetAsInput, IsSetAsOutput, Excluded, ShowGeometry, X, Y }`.
- Top-level `Inputs`/`Outputs` (Dynamo Player) use the **node** Id (not port): `{ Id, Name, Type, Value, Description }`.
- String input: `"CoreNodeModels.Input.StringInput, CoreNodeModels"`, `NodeType: "StringInputNode"`, field `InputValue`. Integer slider: `"CoreNodeModels.Input.IntegerSlider, CoreNodeModels"`, `NodeType: "NumberInputNode"`. Watch: `"CoreNodeModels.Watch, CoreNodeModels"`, `NodeType: "ExtensionNode"`.
- Mandatory top-level keys: `Uuid, IsCustomNode, Inputs, Outputs, Nodes, Connectors, View`. Valid-if-empty: `Dependencies, NodeLibraryDependencies, Bindings, ElementResolver, Name, Description`. Include with defaults: `Linting, ExtensionWorkspaceData, Author, GraphDocumentationURL, EnableLegacyPolyCurveBehavior, Thumbnail`.
- Schema is structurally identical 2.x↔3.x; only `View.Dynamo.Version` and default engine differ. Always write `"Engine": "CPython3"` explicitly (omitting it defaults to deprecated IronPython2).

**Key codebase facts (verified, file:line):**
- Tool registration is reflection-based: `CortexRouter.RegisterToolsFromAssembly(Assembly)` (CortexRouter.cs:82–100) scans for non-abstract `ICortexTool` with a parameterless ctor.
- Plugin loads `RevitCortex.Tools.dll` via `Assembly.LoadFrom` (RevitCortexApp.cs:614–629, called ~line 90). A NEW assembly is NOT auto-discovered — must add an explicit load call.
- Plugin also scans its own assembly (RevitCortexApp.cs:96–99).
- `CortexSettings` at `src/RevitCortex.Core/Security/CortexSettings.cs`; `EnableCodeExecution` flag is the template (lines 18–19).
- `CodeSandbox.Validate(code)` → `CortexResult<object>?` (null = OK). Delegates to `CodeSandboxV2`.
- `ICortexTool`: `Name, Category, RequiresDocument, IsDynamic, Description, Execute(JObject, CortexSession)`.
- `session.RequestConfirmation(string action, int count)` → bool.
- Server tools are static methods with `[McpServerTool(Name=...)]` + `[Description]`, signature `public static async Task<string> X(RevitConnectionManager revit, [Description]params..., CancellationToken ct = default)`, body builds `JObject` and calls `revit.ExecuteAsync("name", p, ct)`. Example: `src/RevitCortex.Server/Tools/MetaTools.cs:8–36`.
- Tests: `src/RevitCortex.Tests/`, net8 single-target, xUnit 2.9.3, references Core/Plugin/Server/Tools. `RequiresRevitApiFactAttribute` at `src/RevitCortex.Tests/RequiresRevitApiFactAttribute.cs`.
- Solution `.sln` GlobalSection has R23–R26 configs only (R27 absent — needs SDK 10; `global.json` rollForward handles it).

**Decision:** separate project `RevitCortex.Tools.Dynamo.dll` (honors "clean/separate code" requirement) + one explicit load call in `RevitCortexApp` (Option B).

---

## File Structure

**New project `src/RevitCortex.Tools.Dynamo/`:**
- `RevitCortex.Tools.Dynamo.csproj` — multi-target, ZERO Dynamo PackageReference
- `Building/GraphPort.cs` — input/output port DTO (net48-safe class)
- `Building/DynamoGraphSpec.cs` — generation spec DTO
- `Building/DynamoValidationResult.cs` — spec validation result DTO
- `Building/DynJsonSchema.cs` — verified ConcreteType/NodeType constants
- `Building/DynamoGraphBuilder.cs` — deterministic `.dyn` JSON builder
- `Building/DynGraphReader.cs` — `.dyn` JSON parser for list_graph_io
- `Security/PythonSandbox.cs` — adapter over `CodeSandboxV2`
- `Runtime/DynamoPaths.cs` — resolve Revit/DynamoForRevit paths + version (no DLL load)
- `Runtime/DynamoCapabilityProbe.cs` — presence/version/engine probe
- `Runtime/DynamoRuntimeLoader.cs` — lazy reflection load of Dynamo DLLs
- `Tools/DynamoGetStatusTool.cs`
- `Tools/DynamoListGraphIoTool.cs`
- `Tools/DynamoGenerateGraphTool.cs`
- `Tools/DynamoRunGraphTool.cs`

**Modified:**
- `src/RevitCortex.Core/Security/CortexSettings.cs` — add `EnableDynamo`
- `RevitCortex.sln` — declare new project + config mappings
- `src/RevitCortex.Plugin/RevitCortex.Plugin.csproj` — ProjectReference (build ordering only)
- `src/RevitCortex.Plugin/RevitCortexApp.cs` — explicit load of Tools.Dynamo assembly
- `src/RevitCortex.Tests/RevitCortex.Tests.csproj` — ProjectReference to Tools.Dynamo
- `src/RevitCortex.Server/Tools/DynamoTools.cs` — 4 server wrappers (new file)
- `src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml(.cs)` — Enable Dynamo checkbox
- Docs: `USER_GUIDE.md`, `tool-schemas.txt`, `CLAUDE.md` (routing rule)

**New tests `src/RevitCortex.Tests/Dynamo/`:**
- `DynamoGraphBuilderTests.cs`, `DynGraphReaderTests.cs`, `PythonSandboxTests.cs`, `DynamoPathsTests.cs`, `CortexSettingsDynamoTests.cs`

---

## Phasing

Development follows the spec roadmap on this single branch:
- **Phase A (Tasks 1–14):** everything except live headless run — builder, reader, sandbox, probe, settings, all 4 tools' logic, server wrappers, UI, docs. Fully unit-testable without Revit/Dynamo. Validate on Debug R25 + Debug R24.
- **Phase B (Task 15):** net48 (R23/R24) + net10 (R27) build parity.
- **Phase C (Task 16):** live headless run verification on a machine with Dynamo (R25). Documented as a manual verification task.

---

## Task 1: Scaffold the new project (compiles empty)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj`
- Create: `src/RevitCortex.Tools.Dynamo/Building/DynJsonSchema.cs` (placeholder type so the project has content)

- [ ] **Step 1: Create the csproj mirroring RevitCortex.Tools.csproj (minus Dynamo/ClosedXML deps)**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <Configurations>Debug R23;Debug R24;Debug R25;Debug R26;Debug R27;Release R23;Release R24;Release R25;Release R26;Release R27</Configurations>
    <LangVersion>latest</LangVersion>
    <RootNamespace>RevitCortex.Tools.Dynamo</RootNamespace>
    <AssemblyName>RevitCortex.Tools.Dynamo</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)'=='Debug R23' Or '$(Configuration)'=='Release R23'">
    <TargetFramework>net48</TargetFramework>
    <RevitVersion>2023</RevitVersion>
    <DefineConstants>$(DefineConstants);REVIT2023_OR_GREATER</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)'=='Debug R24' Or '$(Configuration)'=='Release R24'">
    <TargetFramework>net48</TargetFramework>
    <RevitVersion>2024</RevitVersion>
    <DefineConstants>$(DefineConstants);REVIT2023_OR_GREATER;REVIT2024_OR_GREATER</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)'=='Debug R25' Or '$(Configuration)'=='Release R25'">
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <RevitVersion>2025</RevitVersion>
    <DefineConstants>$(DefineConstants);REVIT2023_OR_GREATER;REVIT2024_OR_GREATER;REVIT2025_OR_GREATER</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)'=='Debug R26' Or '$(Configuration)'=='Release R26'">
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <RevitVersion>2026</RevitVersion>
    <DefineConstants>$(DefineConstants);REVIT2023_OR_GREATER;REVIT2024_OR_GREATER;REVIT2025_OR_GREATER;REVIT2026_OR_GREATER</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)'=='Debug R27' Or '$(Configuration)'=='Release R27'">
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RevitVersion>2027</RevitVersion>
    <DefineConstants>$(DefineConstants);REVIT2023_OR_GREATER;REVIT2024_OR_GREATER;REVIT2025_OR_GREATER;REVIT2026_OR_GREATER;REVIT2027_OR_GREATER</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="$(RevitVersion).*" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="$(RevitVersion).*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RevitCortex.Core\RevitCortex.Core.csproj" />
  </ItemGroup>

</Project>
```

Note: NO `DynamoVisualProgramming.*` PackageReference — all Dynamo access is via reflection. RevitAPI is referenced (reference-only via Nice3point) so tools can use `Document` in signatures if needed; the builder itself does not touch Revit types.

- [ ] **Step 2: Add a placeholder schema constants file so the project has content**

```csharp
namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>Verified .dyn JSON schema constants (source: DynamoDS/Dynamo master).</summary>
    public static class DynJsonSchema
    {
        public const string PythonNodeConcreteType = "PythonNodeModels.PythonNode, PythonNodeModels";
        public const string PythonNodeType = "PythonScriptNode";
        public const string EngineCPython3 = "CPython3";
        public const string StringInputConcreteType = "CoreNodeModels.Input.StringInput, CoreNodeModels";
        public const string StringInputNodeType = "StringInputNode";
        public const string IntegerSliderConcreteType = "CoreNodeModels.Input.IntegerSlider, CoreNodeModels";
        public const string NumberInputNodeType = "NumberInputNode";
        public const string WatchConcreteType = "CoreNodeModels.Watch, CoreNodeModels";
        public const string WatchNodeType = "ExtensionNode";
    }
}
```

- [ ] **Step 3: Build the new project for R25 and R24 to verify the csproj is valid**

Run:
```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
```
Expected: both `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/
git commit -m "feat(dynamo): scaffold RevitCortex.Tools.Dynamo project"
```

---

## Task 2: Add the project to the solution and test project reference

**Files:**
- Modify: `RevitCortex.sln`
- Modify: `src/RevitCortex.Tests/RevitCortex.Tests.csproj:14-19`

- [ ] **Step 1: Add the project declaration to RevitCortex.sln**

After the `RevitCortex.Tools` project block (RevitCortex.sln:14-16), add:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "RevitCortex.Tools.Dynamo", "src\RevitCortex.Tools.Dynamo\RevitCortex.Tools.Dynamo.csproj", "{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}"
EndProject
```

- [ ] **Step 2: Add config mappings in GlobalSection(ProjectConfigurationPlatforms)**

Mirror the R23–R26 entries that exist for RevitCortex.Tools (RevitCortex.sln:232-279). Add these lines inside that section (R27 is intentionally omitted to match the solution's existing R23–R26 coverage; the csproj still supports R27 for direct `dotnet build`):

```
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R23|Any CPU.ActiveCfg = Debug R23|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R23|Any CPU.Build.0 = Debug R23|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R24|Any CPU.ActiveCfg = Debug R24|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R24|Any CPU.Build.0 = Debug R24|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R25|Any CPU.ActiveCfg = Debug R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R25|Any CPU.Build.0 = Debug R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R26|Any CPU.ActiveCfg = Debug R26|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug R26|Any CPU.Build.0 = Debug R26|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R23|Any CPU.ActiveCfg = Release R23|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R23|Any CPU.Build.0 = Release R23|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R24|Any CPU.ActiveCfg = Release R24|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R24|Any CPU.Build.0 = Release R24|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R25|Any CPU.ActiveCfg = Release R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R25|Any CPU.Build.0 = Release R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R26|Any CPU.ActiveCfg = Release R26|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release R26|Any CPU.Build.0 = Release R26|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug|Any CPU.ActiveCfg = Debug R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Debug|Any CPU.Build.0 = Debug R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release|Any CPU.ActiveCfg = Release R25|Any CPU
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC}.Release|Any CPU.Build.0 = Release R25|Any CPU
```

- [ ] **Step 3: Nest under the "src" solution folder**

In `GlobalSection(NestedProjects)` (near RevitCortex.sln:360), add:

```
		{D2E3F4A5-B6C7-8901-DEFA-234567890ABC} = {827E0CD3-B72D-47B6-A68D-7590B98EB39B}
```

- [ ] **Step 4: Add ProjectReference in the test project**

In `src/RevitCortex.Tests/RevitCortex.Tests.csproj`, inside the existing `<ItemGroup>` with the ProjectReferences (lines 14-19), add:

```xml
    <ProjectReference Include="..\RevitCortex.Tools.Dynamo\RevitCortex.Tools.Dynamo.csproj" />
```

- [ ] **Step 5: Build the test project to verify the reference resolves**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj`
Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add RevitCortex.sln src/RevitCortex.Tests/RevitCortex.Tests.csproj
git commit -m "feat(dynamo): wire Tools.Dynamo into solution and test project"
```

---

## Task 3: `EnableDynamo` setting (TDD)

**Files:**
- Modify: `src/RevitCortex.Core/Security/CortexSettings.cs`
- Test: `src/RevitCortex.Tests/Dynamo/CortexSettingsDynamoTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/RevitCortex.Tests/Dynamo/CortexSettingsDynamoTests.cs`:

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class CortexSettingsDynamoTests
    {
        [Fact]
        public void EnableDynamo_DefaultsToFalse()
        {
            var s = new CortexSettings();
            Assert.False(s.EnableDynamo);
        }

        [Fact]
        public void EnableDynamo_RoundTripsThroughJson()
        {
            var path = Path.Combine(Path.GetTempPath(), "rc_dyn_settings_" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new CortexSettings { EnableDynamo = true };
                s.Save(path);
                var loaded = CortexSettings.Load(path);
                Assert.True(loaded.EnableDynamo);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void EnableDynamo_SerializesWithExpectedJsonName()
        {
            var s = new CortexSettings { EnableDynamo = true };
            var json = JObject.FromObject(s);
            Assert.True((bool)json["EnableDynamo"]!);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexSettingsDynamoTests"`
Expected: FAIL — `CortexSettings` has no `EnableDynamo` member (compile error).

- [ ] **Step 3: Add the flag**

In `src/RevitCortex.Core/Security/CortexSettings.cs`, after the `EnableCodeExecution` property (line 19), add:

```csharp
    /// <summary>
    /// When false (default), the Dynamo write tools (dynamo_generate_graph, dynamo_run_graph)
    /// are refused at the tool-invocation boundary. The user must explicitly enable Dynamo
    /// integration via settings.json or the Revit plugin Settings UI. Hard gate, not a soft warning.
    /// </summary>
    [JsonProperty("EnableDynamo")]
    public bool EnableDynamo { get; set; } = false;
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexSettingsDynamoTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Core/Security/CortexSettings.cs src/RevitCortex.Tests/Dynamo/CortexSettingsDynamoTests.cs
git commit -m "feat(dynamo): add EnableDynamo settings gate (default false)"
```

---

## Task 4: DTOs — `GraphPort`, `DynamoGraphSpec`, `DynamoValidationResult`

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Building/GraphPort.cs`
- Create: `src/RevitCortex.Tools.Dynamo/Building/DynamoGraphSpec.cs`
- Create: `src/RevitCortex.Tools.Dynamo/Building/DynamoValidationResult.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoSpecTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/RevitCortex.Tests/Dynamo/DynamoSpecTests.cs`:

```csharp
using System.Collections.Generic;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoSpecTests
    {
        [Fact]
        public void GraphPort_StoresNameAndType()
        {
            var p = new GraphPort("folderPath", "String");
            Assert.Equal("folderPath", p.Name);
            Assert.Equal("String", p.Type);
        }

        [Fact]
        public void DynamoGraphSpec_DefaultsEngineToCPython3()
        {
            var spec = new DynamoGraphSpec(
                "G", "OUT = 1",
                new List<GraphPort>(), new List<GraphPort>());
            Assert.Equal("CPython3", spec.Engine);
        }

        [Fact]
        public void DynamoValidationResult_OkHasNoErrors()
        {
            var r = DynamoValidationResult.Ok();
            Assert.True(r.IsValid);
            Assert.Empty(r.Errors);
        }

        [Fact]
        public void DynamoValidationResult_FailCarriesErrors()
        {
            var r = DynamoValidationResult.Fail("bad name", "empty code");
            Assert.False(r.IsValid);
            Assert.Equal(2, r.Errors.Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoSpecTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement the DTOs (net48-safe: plain classes, ctor init, no records/init)**

`Building/GraphPort.cs`:

```csharp
namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>A typed graph input/output port (e.g. name "limit", type "Integer").</summary>
    public sealed class GraphPort
    {
        public string Name { get; }
        public string Type { get; }   // "String" | "Integer" | "Number" | "Boolean" | "Filename"

        public GraphPort(string name, string type)
        {
            Name = name ?? "";
            Type = string.IsNullOrEmpty(type) ? "String" : type;
        }
    }
}
```

`Building/DynamoGraphSpec.cs`:

```csharp
using System.Collections.Generic;

namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>Everything needed to generate a Python-centric .dyn skeleton.</summary>
    public sealed class DynamoGraphSpec
    {
        public string Name { get; }
        public string PythonCode { get; }
        public IReadOnlyList<GraphPort> Inputs { get; }
        public IReadOnlyList<GraphPort> Outputs { get; }
        public string Engine { get; }

        public DynamoGraphSpec(
            string name,
            string pythonCode,
            IReadOnlyList<GraphPort> inputs,
            IReadOnlyList<GraphPort> outputs,
            string engine = "CPython3")
        {
            Name = string.IsNullOrEmpty(name) ? "RevitCortexGraph" : name;
            PythonCode = pythonCode ?? "";
            Inputs = inputs ?? new List<GraphPort>();
            Outputs = outputs ?? new List<GraphPort>();
            Engine = string.IsNullOrEmpty(engine) ? "CPython3" : engine;
        }
    }
}
```

`Building/DynamoValidationResult.cs`:

```csharp
using System.Collections.Generic;

namespace RevitCortex.Tools.Dynamo.Building
{
    public sealed class DynamoValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }

        private DynamoValidationResult(bool isValid, IReadOnlyList<string> errors)
        {
            IsValid = isValid;
            Errors = errors;
        }

        public static DynamoValidationResult Ok()
            => new DynamoValidationResult(true, new List<string>());

        public static DynamoValidationResult Fail(params string[] errors)
            => new DynamoValidationResult(false, new List<string>(errors));
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoSpecTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Building/ src/RevitCortex.Tests/Dynamo/DynamoSpecTests.cs
git commit -m "feat(dynamo): add graph spec DTOs"
```

---

## Task 5: `DynamoGraphBuilder` — spec validation (TDD)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Building/DynamoGraphBuilder.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoGraphBuilderValidateTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/RevitCortex.Tests/Dynamo/DynamoGraphBuilderValidateTests.cs`:

```csharp
using System.Collections.Generic;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoGraphBuilderValidateTests
    {
        private static DynamoGraphSpec Spec(string name, string code,
            List<GraphPort> ins = null, List<GraphPort> outs = null)
            => new DynamoGraphSpec(name, code, ins ?? new List<GraphPort>(), outs ?? new List<GraphPort>());

        [Fact]
        public void ValidateSpec_RejectsEmptyPythonCode()
        {
            var b = new DynamoGraphBuilder();
            var r = b.ValidateSpec(Spec("G", "   "));
            Assert.False(r.IsValid);
        }

        [Fact]
        public void ValidateSpec_RejectsDuplicateInputNames()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "String"), new GraphPort("x", "Integer") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
        }

        [Fact]
        public void ValidateSpec_RejectsUnknownPortType()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("x", "Banana") };
            var r = b.ValidateSpec(Spec("G", "OUT = IN[0]", ins));
            Assert.False(r.IsValid);
        }

        [Fact]
        public void ValidateSpec_AcceptsValidSpec()
        {
            var b = new DynamoGraphBuilder();
            var ins = new List<GraphPort> { new GraphPort("folder", "String"), new GraphPort("limit", "Integer") };
            var outs = new List<GraphPort> { new GraphPort("result", "String") };
            var r = b.ValidateSpec(Spec("Export", "OUT = IN[0]", ins, outs));
            Assert.True(r.IsValid);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoGraphBuilderValidateTests"`
Expected: FAIL — `DynamoGraphBuilder` not defined.

- [ ] **Step 3: Implement builder with ValidateSpec (BuildDynJson stubbed for now)**

Create `src/RevitCortex.Tools.Dynamo/Building/DynamoGraphBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>
    /// Deterministically builds a valid Python-centric .dyn JSON document.
    /// Never loads any Dynamo DLL — pure string/JSON construction.
    /// </summary>
    public sealed class DynamoGraphBuilder
    {
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Number", "Boolean", "Filename"
        };

        public DynamoValidationResult ValidateSpec(DynamoGraphSpec spec)
        {
            var errors = new List<string>();
            if (spec == null) return DynamoValidationResult.Fail("spec is null");

            if (string.IsNullOrWhiteSpace(spec.PythonCode))
                errors.Add("pythonCode is empty");

            CheckPorts("input", spec.Inputs, errors);
            CheckPorts("output", spec.Outputs, errors);

            return errors.Count == 0
                ? DynamoValidationResult.Ok()
                : DynamoValidationResult.Fail(errors.ToArray());
        }

        private static void CheckPorts(string kind, IReadOnlyList<GraphPort> ports, List<string> errors)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ports)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                    errors.Add($"{kind} port has empty name");
                else if (!seen.Add(p.Name))
                    errors.Add($"duplicate {kind} port name: {p.Name}");
                if (!AllowedTypes.Contains(p.Type))
                    errors.Add($"unknown {kind} port type: {p.Type}");
            }
        }

        // Implemented in Task 6.
        public string BuildDynJson(DynamoGraphSpec spec)
        {
            throw new NotImplementedException("BuildDynJson is implemented in Task 6");
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoGraphBuilderValidateTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Building/DynamoGraphBuilder.cs src/RevitCortex.Tests/Dynamo/DynamoGraphBuilderValidateTests.cs
git commit -m "feat(dynamo): DynamoGraphBuilder spec validation"
```

---

## Task 6: `DynamoGraphBuilder.BuildDynJson` — deterministic .dyn generation (TDD)

**Files:**
- Modify: `src/RevitCortex.Tools.Dynamo/Building/DynamoGraphBuilder.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoGraphBuilderBuildTests.cs`

- [ ] **Step 1: Write the failing test (round-trip + schema invariants)**

Create `src/RevitCortex.Tests/Dynamo/DynamoGraphBuilderBuildTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoGraphBuilderBuildTests
    {
        private static JObject Build(DynamoGraphSpec spec)
            => JObject.Parse(new DynamoGraphBuilder().BuildDynJson(spec));

        private static DynamoGraphSpec Sample()
            => new DynamoGraphSpec(
                "ExportRooms",
                "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("folder", "String"), new GraphPort("limit", "Integer") },
                new List<GraphPort> { new GraphPort("result", "String") });

        [Fact]
        public void Build_ProducesParseableJson_WithMandatoryTopLevelKeys()
        {
            var j = Build(Sample());
            foreach (var key in new[] { "Uuid", "IsCustomNode", "Inputs", "Outputs", "Nodes", "Connectors", "View" })
                Assert.True(j[key] != null, $"missing top-level key {key}");
        }

        [Fact]
        public void Build_HasExactlyOnePythonNode_WithCPython3Engine()
        {
            var j = Build(Sample());
            var py = ((JArray)j["Nodes"]).Single(n =>
                (string)n["ConcreteType"] == DynJsonSchema.PythonNodeConcreteType);
            Assert.Equal("CPython3", (string)py["Engine"]);
            Assert.Equal("PythonScriptNode", (string)py["NodeType"]);
            Assert.True((bool)py["VariableInputPorts"]);
        }

        [Fact]
        public void Build_CreatesInputNodesAndWatchOutputNodes()
        {
            var j = Build(Sample());
            var nodes = (JArray)j["Nodes"];
            // 2 inputs + 1 python + 1 watch = 4
            Assert.Equal(4, nodes.Count);
            Assert.Equal(1, nodes.Count(n => (string)n["ConcreteType"] == DynJsonSchema.WatchConcreteType));
            Assert.Equal(2, nodes.Count(n =>
                (string)n["ConcreteType"] == DynJsonSchema.StringInputConcreteType
                || (string)n["ConcreteType"] == DynJsonSchema.IntegerSliderConcreteType));
        }

        [Fact]
        public void Build_EveryNodeHasMatchingNodeView()
        {
            var j = Build(Sample());
            var nodeIds = ((JArray)j["Nodes"]).Select(n => (string)n["Id"]).ToHashSet();
            var viewIds = ((JArray)j["View"]["NodeViews"]).Select(v => (string)v["Id"]).ToHashSet();
            Assert.Equal(nodeIds, viewIds);
        }

        [Fact]
        public void Build_ConnectorsReferenceExistingPortIds_AndHaveNoConcreteType()
        {
            var j = Build(Sample());
            var portIds = new HashSet<string>();
            foreach (var n in (JArray)j["Nodes"])
            {
                foreach (var p in (JArray)n["Inputs"]) portIds.Add((string)p["Id"]);
                foreach (var p in (JArray)n["Outputs"]) portIds.Add((string)p["Id"]);
            }
            foreach (var c in (JArray)j["Connectors"])
            {
                Assert.Null(c["ConcreteType"]); // Connector must NOT carry ConcreteType
                Assert.Contains((string)c["Start"], portIds);
                Assert.Contains((string)c["End"], portIds);
            }
        }

        [Fact]
        public void Build_TopLevelInputsReferenceNodeIds()
        {
            var j = Build(Sample());
            var nodeIds = ((JArray)j["Nodes"]).Select(n => (string)n["Id"]).ToHashSet();
            foreach (var inp in (JArray)j["Inputs"])
                Assert.Contains((string)inp["Id"], nodeIds);
        }

        [Fact]
        public void Build_PythonNodeHasOneInputPortPerSpecInput()
        {
            var j = Build(Sample());
            var py = ((JArray)j["Nodes"]).Single(n =>
                (string)n["ConcreteType"] == DynJsonSchema.PythonNodeConcreteType);
            Assert.Equal(2, ((JArray)py["Inputs"]).Count);
            Assert.Equal(1, ((JArray)py["Outputs"]).Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoGraphBuilderBuildTests"`
Expected: FAIL — `BuildDynJson` throws `NotImplementedException`.

- [ ] **Step 3: Implement BuildDynJson**

Replace the `BuildDynJson` stub in `src/RevitCortex.Tools.Dynamo/Building/DynamoGraphBuilder.cs` with the full implementation, and add the `using`/helpers. Full method (uses `Guid.NewGuid().ToString("N")` for 32-hex ids):

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
// (keep existing usings: System, System.Collections.Generic)
```

```csharp
        public string BuildDynJson(DynamoGraphSpec spec)
        {
            var nodes = new JArray();
            var connectors = new JArray();
            var nodeViews = new JArray();
            var topInputs = new JArray();
            var topOutputs = new JArray();

            // Python node: create its input ports (one per spec input) and one output port.
            string pyId = NewId();
            var pyInputPorts = new JArray();
            var pyInputPortIds = new List<string>();
            for (int i = 0; i < spec.Inputs.Count; i++)
            {
                string portId = NewId();
                pyInputPortIds.Add(portId);
                pyInputPorts.Add(Port(portId, "IN" + i, "Input #" + i));
            }
            string pyOutPortId = NewId();
            var pyOutputPorts = new JArray { Port(pyOutPortId, "OUT", "Result of the python script") };

            // Input nodes (String/Integer/etc.) + connectors input->python.
            int y = 0;
            for (int i = 0; i < spec.Inputs.Count; i++)
            {
                var gp = spec.Inputs[i];
                string nodeId = NewId();
                string outPortId = NewId();
                var inputNode = InputNode(gp, nodeId, outPortId);
                nodes.Add(inputNode);
                nodeViews.Add(NodeView(nodeId, gp.Name, 0, y, isInput: true));
                connectors.Add(Connector(outPortId, pyInputPortIds[i]));
                topInputs.Add(TopInput(nodeId, gp));
                y += 150;
            }

            // Python node itself.
            var pyNode = new JObject
            {
                ["ConcreteType"] = DynJsonSchema.PythonNodeConcreteType,
                ["Code"] = NormalizeNewlines(spec.PythonCode),
                ["Engine"] = string.IsNullOrEmpty(spec.Engine) ? DynJsonSchema.EngineCPython3 : spec.Engine,
                ["VariableInputPorts"] = true,
                ["Id"] = pyId,
                ["NodeType"] = DynJsonSchema.PythonNodeType,
                ["Inputs"] = pyInputPorts,
                ["Outputs"] = pyOutputPorts,
                ["Replication"] = "Disabled",
                ["Description"] = "Runs an embedded Python script."
            };
            nodes.Add(pyNode);
            nodeViews.Add(NodeView(pyId, "Python Script", 350, 0, isInput: false));

            // Output (Watch) nodes + connectors python->watch.
            int wy = 0;
            foreach (var gp in spec.Outputs)
            {
                string watchId = NewId();
                string watchInId = NewId();
                string watchOutId = NewId();
                var watch = new JObject
                {
                    ["ConcreteType"] = DynJsonSchema.WatchConcreteType,
                    ["Id"] = watchId,
                    ["NodeType"] = DynJsonSchema.WatchNodeType,
                    ["Inputs"] = new JArray { Port(watchInId, "", "Node to evaluate.") },
                    ["Outputs"] = new JArray { Port(watchOutId, "", "Watch contents.") },
                    ["Description"] = "Visualizes a node's output"
                };
                nodes.Add(watch);
                nodeViews.Add(NodeView(watchId, gp.Name, 700, wy, isInput: false, isOutput: true));
                connectors.Add(Connector(pyOutPortId, watchInId));
                topOutputs.Add(new JObject { ["Id"] = watchId, ["Name"] = gp.Name });
                wy += 150;
            }

            var doc = new JObject
            {
                ["Uuid"] = System.Guid.NewGuid().ToString(),
                ["IsCustomNode"] = false,
                ["Description"] = "",
                ["Name"] = spec.Name,
                ["ElementResolver"] = new JObject { ["ResolutionMap"] = new JObject() },
                ["Inputs"] = topInputs,
                ["Outputs"] = topOutputs,
                ["Nodes"] = nodes,
                ["Connectors"] = connectors,
                ["Dependencies"] = new JArray(),
                ["NodeLibraryDependencies"] = new JArray(),
                ["EnableLegacyPolyCurveBehavior"] = true,
                ["Thumbnail"] = "",
                ["GraphDocumentationURL"] = null,
                ["ExtensionWorkspaceData"] = new JArray(),
                ["Author"] = "RevitCortex",
                ["Linting"] = new JObject
                {
                    ["activeLinter"] = "None",
                    ["activeLinterId"] = "7b75fb44-43fd-4631-a878-29f4d5d8399a",
                    ["warningCount"] = 0,
                    ["errorCount"] = 0
                },
                ["Bindings"] = new JArray(),
                ["View"] = new JObject
                {
                    ["Dynamo"] = new JObject
                    {
                        ["ScaleFactor"] = 1.0,
                        ["HasRunWithoutCrash"] = true,
                        ["IsVisibleInDynamoLibrary"] = true,
                        ["Version"] = "3.0.0.0",
                        ["RunType"] = "Automatic",
                        ["RunPeriod"] = "1000"
                    },
                    ["Camera"] = new JObject
                    {
                        ["Name"] = "_Background Preview",
                        ["EyeX"] = -17.0, ["EyeY"] = 24.0, ["EyeZ"] = 50.0,
                        ["LookX"] = 12.0, ["LookY"] = -13.0, ["LookZ"] = -58.0,
                        ["UpX"] = 0.0, ["UpY"] = 1.0, ["UpZ"] = 0.0
                    },
                    ["ConnectorPins"] = new JArray(),
                    ["NodeViews"] = nodeViews,
                    ["Annotations"] = new JArray(),
                    ["X"] = 0.0,
                    ["Y"] = 0.0,
                    ["Zoom"] = 1.0
                }
            };

            return doc.ToString(Formatting.Indented);
        }

        private static string NewId() => System.Guid.NewGuid().ToString("N");

        private static string NormalizeNewlines(string code)
            => (code ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        private static JObject Port(string id, string name, string description) => new JObject
        {
            ["Id"] = id,
            ["Name"] = name,
            ["Description"] = description,
            ["UsingDefaultValue"] = false,
            ["Level"] = 2,
            ["UseLevels"] = false,
            ["KeepListStructure"] = false
        };

        private static JObject Connector(string startPortId, string endPortId) => new JObject
        {
            ["Start"] = startPortId,
            ["End"] = endPortId,
            ["Id"] = NewId(),
            ["IsHidden"] = "False"
        };

        private static JObject NodeView(string id, string name, double x, double y,
            bool isInput = false, bool isOutput = false) => new JObject
        {
            ["Id"] = id,
            ["Name"] = string.IsNullOrEmpty(name) ? "Node" : name,
            ["IsSetAsInput"] = isInput,
            ["IsSetAsOutput"] = isOutput,
            ["Excluded"] = false,
            ["ShowGeometry"] = true,
            ["X"] = x,
            ["Y"] = y
        };

        private static JObject InputNode(GraphPort gp, string nodeId, string outPortId)
        {
            if (string.Equals(gp.Type, "Integer", System.StringComparison.OrdinalIgnoreCase))
            {
                return new JObject
                {
                    ["ConcreteType"] = DynJsonSchema.IntegerSliderConcreteType,
                    ["NumberType"] = "Integer",
                    ["MaximumValue"] = 100,
                    ["MinimumValue"] = 0,
                    ["StepValue"] = 1,
                    ["Id"] = nodeId,
                    ["NodeType"] = DynJsonSchema.NumberInputNodeType,
                    ["Inputs"] = new JArray(),
                    ["Outputs"] = new JArray { Port(outPortId, "", "Int64") },
                    ["Replication"] = "Disabled",
                    ["Description"] = "Produces integer values",
                    ["InputValue"] = 0
                };
            }
            // String / Number / Boolean / Filename all render as a StringInput carrying text;
            // Dynamo coerces on the Python side. Keeps the skeleton uniform and always-valid.
            return new JObject
            {
                ["ConcreteType"] = DynJsonSchema.StringInputConcreteType,
                ["NodeType"] = DynJsonSchema.StringInputNodeType,
                ["InputValue"] = "",
                ["Id"] = nodeId,
                ["Inputs"] = new JArray(),
                ["Outputs"] = new JArray { Port(outPortId, "", "String") }
            };
        }

        private static JObject TopInput(string nodeId, GraphPort gp) => new JObject
        {
            ["Id"] = nodeId,
            ["Name"] = gp.Name,
            ["Type"] = gp.Type.ToLowerInvariant(),
            ["Value"] = "",
            ["Description"] = "RevitCortex graph input"
        };
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoGraphBuilderBuildTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Build R24 (net48) to confirm cross-target compile**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj`
Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Building/DynamoGraphBuilder.cs src/RevitCortex.Tests/Dynamo/DynamoGraphBuilderBuildTests.cs
git commit -m "feat(dynamo): deterministic .dyn generation in DynamoGraphBuilder"
```

---

## Task 7: `DynGraphReader` — parse a .dyn for list_graph_io (TDD)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Building/DynGraphReader.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynGraphReaderTests.cs`

- [ ] **Step 1: Write the failing test (round-trips a builder-produced .dyn)**

Create `src/RevitCortex.Tests/Dynamo/DynGraphReaderTests.cs`:

```csharp
using System.Collections.Generic;
using RevitCortex.Tools.Dynamo.Building;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynGraphReaderTests
    {
        private static string BuildSampleDyn()
            => new DynamoGraphBuilder().BuildDynJson(new DynamoGraphSpec(
                "RoundTrip",
                "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("folder", "String") },
                new List<GraphPort> { new GraphPort("result", "String") }));

        [Fact]
        public void Read_ExtractsNameAndEngineAndCounts()
        {
            var info = DynGraphReader.Read(BuildSampleDyn());
            Assert.Equal("RoundTrip", info.Name);
            Assert.Equal("CPython3", info.PythonEngine);
            Assert.Equal(1, info.PythonNodeCount);
            Assert.Equal(3, info.TotalNodes); // 1 input + 1 python + 1 watch
        }

        [Fact]
        public void Read_ListsInputsAndOutputs()
        {
            var info = DynGraphReader.Read(BuildSampleDyn());
            Assert.Single(info.Inputs);
            Assert.Equal("folder", info.Inputs[0].Name);
            Assert.Single(info.Outputs);
            Assert.Equal("result", info.Outputs[0].Name);
        }

        [Fact]
        public void Read_WarnsOnMissingEngine_InterpretedAsIronPython2()
        {
            // A hand-made python node WITHOUT an Engine field.
            var dyn = "{\"Nodes\":[{\"ConcreteType\":\"PythonNodeModels.PythonNode, PythonNodeModels\",\"NodeType\":\"PythonScriptNode\"}],\"Inputs\":[],\"Outputs\":[],\"View\":{\"NodeViews\":[]}}";
            var info = DynGraphReader.Read(dyn);
            Assert.Contains(info.Warnings, w => w.Contains("IronPython2"));
        }

        [Fact]
        public void Read_ThrowsOnInvalidJson()
        {
            Assert.ThrowsAny<System.Exception>(() => DynGraphReader.Read("{ not json"));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynGraphReaderTests"`
Expected: FAIL — `DynGraphReader` not defined.

- [ ] **Step 3: Implement DynGraphReader**

Create `src/RevitCortex.Tools.Dynamo/Building/DynGraphReader.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Dynamo.Building
{
    public sealed class GraphIoEntry
    {
        public string NodeId { get; }
        public string Name { get; }
        public string Type { get; }
        public string Value { get; }
        public GraphIoEntry(string nodeId, string name, string type, string value)
        { NodeId = nodeId; Name = name; Type = type; Value = value; }
    }

    public sealed class DynGraphInfo
    {
        public string Name { get; internal set; } = "";
        public string DynamoVersion { get; internal set; } = "";
        public string PythonEngine { get; internal set; } = "";
        public int PythonNodeCount { get; internal set; }
        public int TotalNodes { get; internal set; }
        public List<GraphIoEntry> Inputs { get; } = new List<GraphIoEntry>();
        public List<GraphIoEntry> Outputs { get; } = new List<GraphIoEntry>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Parses a .dyn (JSON only — never loads Dynamo) to expose its I/O interface.</summary>
    public static class DynGraphReader
    {
        public static DynGraphInfo Read(string dynJson)
        {
            var j = JObject.Parse(dynJson); // throws on invalid json (tested)
            var info = new DynGraphInfo
            {
                Name = (string)j["Name"] ?? "",
                DynamoVersion = (string)(j["View"]?["Dynamo"]?["Version"]) ?? ""
            };

            var nodes = j["Nodes"] as JArray ?? new JArray();
            info.TotalNodes = nodes.Count;
            foreach (var n in nodes)
            {
                var ct = (string)n["ConcreteType"] ?? "";
                if (ct.StartsWith("PythonNodeModels.PythonNode"))
                {
                    info.PythonNodeCount++;
                    var engine = (string)n["Engine"];
                    if (string.IsNullOrEmpty(engine))
                    {
                        engine = "IronPython2";
                        info.Warnings.Add("A Python node has no Engine field; Dynamo interprets it as deprecated IronPython2.");
                    }
                    if (string.IsNullOrEmpty(info.PythonEngine))
                        info.PythonEngine = engine;
                }
            }

            foreach (var inp in (j["Inputs"] as JArray ?? new JArray()))
                info.Inputs.Add(new GraphIoEntry(
                    (string)inp["Id"] ?? "", (string)inp["Name"] ?? "",
                    (string)inp["Type"] ?? "", (string)(inp["Value"]) ?? ""));

            foreach (var outp in (j["Outputs"] as JArray ?? new JArray()))
                info.Outputs.Add(new GraphIoEntry(
                    (string)outp["Id"] ?? "", (string)outp["Name"] ?? "",
                    (string)outp["Type"] ?? "", ""));

            return info;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynGraphReaderTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Building/DynGraphReader.cs src/RevitCortex.Tests/Dynamo/DynGraphReaderTests.cs
git commit -m "feat(dynamo): DynGraphReader parses .dyn I/O interface"
```

---

## Task 8: `PythonSandbox` — reuse CodeSandboxV2 (TDD)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Security/PythonSandbox.cs`
- Test: `src/RevitCortex.Tests/Dynamo/PythonSandboxTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/RevitCortex.Tests/Dynamo/PythonSandboxTests.cs`:

```csharp
using RevitCortex.Tools.Dynamo.Security;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class PythonSandboxTests
    {
        [Fact]
        public void Validate_AllowsCleanPython()
        {
            var err = PythonSandbox.Validate("OUT = IN[0] + 1");
            Assert.Null(err);
        }

        [Fact]
        public void Validate_BlocksSystemIo()
        {
            var err = PythonSandbox.Validate("import System.IO\nSystem.IO.File.Delete('x')");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksSystemNet()
        {
            var err = PythonSandbox.Validate("clr.AddReference('System.Net')\nSystem.Net.WebClient()");
            Assert.NotNull(err);
        }

        [Fact]
        public void Validate_BlocksProcessStart()
        {
            var err = PythonSandbox.Validate("System.Diagnostics.Process.Start('cmd')");
            Assert.NotNull(err);
        }
    }
}
```

Note: `PythonSandbox.Validate` returns `CortexResult<object>?` — null = clean, non-null = violation. It delegates to `CodeSandboxV2.Validate`, whose namespace blocklist (`System.IO`, `System.Net`, `System.Diagnostics.Process`, `Microsoft.Win32`, `System.Reflection.Emit`, `System.Runtime.InteropServices`) matches these test cases.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~PythonSandboxTests"`
Expected: FAIL — `PythonSandbox` not defined.

- [ ] **Step 3: Implement PythonSandbox**

Create `src/RevitCortex.Tools.Dynamo/Security/PythonSandbox.cs`:

```csharp
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;

namespace RevitCortex.Tools.Dynamo.Security
{
    /// <summary>
    /// Validates generated Python before it is written into a .dyn. Reuses the same
    /// namespace blocklist as send_code_to_revit (CodeSandboxV2). Returns null when clean.
    /// Note: this guards the automated AI->generate->run channel only; a user who opens
    /// the .dyn by hand in Dynamo can still run anything.
    /// </summary>
    public static class PythonSandbox
    {
        public static CortexResult<object>? Validate(string pythonCode)
            => CodeSandbox.Validate(pythonCode ?? "");
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~PythonSandboxTests"`
Expected: PASS (4 tests). If `CodeSandboxV2` does not flag one of the patterns (e.g. it targets C# `using` syntax), adjust the test to the actual blocklist behavior rather than weakening the sandbox — the goal is parity with `send_code_to_revit`, not a new policy.

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Security/PythonSandbox.cs src/RevitCortex.Tests/Dynamo/PythonSandboxTests.cs
git commit -m "feat(dynamo): PythonSandbox reuses CodeSandboxV2 blocklist"
```

---

## Task 9: `DynamoPaths` + `DynamoCapabilityProbe` — presence/version (TDD, no DLL load)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Runtime/DynamoPaths.cs`
- Create: `src/RevitCortex.Tools.Dynamo/Runtime/DynamoCapabilityProbe.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoPathsTests.cs`

- [ ] **Step 1: Write the failing test (pure path logic, injectable base dir)**

Create `src/RevitCortex.Tests/Dynamo/DynamoPathsTests.cs`:

```csharp
using System.IO;
using RevitCortex.Tools.Dynamo.Runtime;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoPathsTests
    {
        [Fact]
        public void DynamoForRevitDir_BuildsExpectedPath()
        {
            var p = DynamoPaths.DynamoForRevitDir(@"C:\Program Files\Autodesk", 2025);
            Assert.Equal(@"C:\Program Files\Autodesk\Revit 2025\AddIns\DynamoForRevit", p);
        }

        [Fact]
        public void Probe_ReportsAbsentWhenDllMissing()
        {
            var tempBase = Path.Combine(Path.GetTempPath(), "rc_no_dynamo_" + System.Guid.NewGuid().ToString("N"));
            var probe = new DynamoCapabilityProbe(tempBase);
            var caps = probe.Probe(2025);
            Assert.False(caps.IsPresent);
        }

        [Fact]
        public void Probe_ReportsPresentWhenDllsExist()
        {
            var tempBase = Path.Combine(Path.GetTempPath(), "rc_dynamo_" + System.Guid.NewGuid().ToString("N"));
            var dir = DynamoPaths.DynamoForRevitDir(tempBase, 2025);
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "DynamoRevitDS.dll"), "x");
                File.WriteAllText(Path.Combine(dir, "DynamoCore.dll"), "x");
                var probe = new DynamoCapabilityProbe(tempBase);
                var caps = probe.Probe(2025);
                Assert.True(caps.IsPresent);
                Assert.True(caps.CPython3Expected); // Revit 2025 -> Dynamo 3.x
            }
            finally { Directory.Delete(tempBase, true); }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoPathsTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement DynamoPaths + DynamoCapabilityProbe**

`Runtime/DynamoPaths.cs`:

```csharp
using System.IO;

namespace RevitCortex.Tools.Dynamo.Runtime
{
    /// <summary>Resolves Dynamo-for-Revit install paths without loading any assembly.</summary>
    public static class DynamoPaths
    {
        public static string ProgramFilesAutodesk()
            => Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                "Autodesk");

        public static string DynamoForRevitDir(string autodeskBase, int revitYear)
            => Path.Combine(autodeskBase, "Revit " + revitYear, "AddIns", "DynamoForRevit");
    }
}
```

`Runtime/DynamoCapabilityProbe.cs`:

```csharp
using System.Diagnostics;
using System.IO;

namespace RevitCortex.Tools.Dynamo.Runtime
{
    public sealed class DynamoCapabilities
    {
        public bool IsPresent { get; internal set; }
        public string DynamoVersion { get; internal set; } = "";
        public bool CPython3Expected { get; internal set; }
        public string DynamoForRevitDir { get; internal set; } = "";
    }

    /// <summary>
    /// Detects presence/version of Dynamo for Revit by file inspection only
    /// (FileVersionInfo, no Assembly.Load). Safe to run at document open.
    /// </summary>
    public sealed class DynamoCapabilityProbe
    {
        private readonly string _autodeskBase;

        public DynamoCapabilityProbe(string? autodeskBase = null)
        {
            _autodeskBase = string.IsNullOrEmpty(autodeskBase)
                ? DynamoPaths.ProgramFilesAutodesk()
                : autodeskBase!;
        }

        public DynamoCapabilities Probe(int revitYear)
        {
            var caps = new DynamoCapabilities();
            var dir = DynamoPaths.DynamoForRevitDir(_autodeskBase, revitYear);
            caps.DynamoForRevitDir = dir;

            var revitDs = Path.Combine(dir, "DynamoRevitDS.dll");
            var core = Path.Combine(dir, "DynamoCore.dll");
            if (!File.Exists(revitDs) || !File.Exists(core))
                return caps; // IsPresent stays false

            caps.IsPresent = true;
            try { caps.DynamoVersion = FileVersionInfo.GetVersionInfo(core).FileVersion ?? ""; }
            catch { caps.DynamoVersion = ""; }

            // Revit 2025+ ships Dynamo 3.x where CPython3 is standard.
            caps.CPython3Expected = revitYear >= 2024;
            return caps;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoPathsTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Runtime/ src/RevitCortex.Tests/Dynamo/DynamoPathsTests.cs
git commit -m "feat(dynamo): capability probe by file inspection (no DLL load)"
```

---

## Task 10: `dynamo_get_status` + `dynamo_list_graph_io` tools (TDD)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Tools/DynamoGetStatusTool.cs`
- Create: `src/RevitCortex.Tools.Dynamo/Tools/DynamoListGraphIoTool.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoReadToolsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/RevitCortex.Tests/Dynamo/DynamoReadToolsTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Dynamo.Building;
using RevitCortex.Tools.Dynamo.Tools;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoReadToolsTests
    {
        private static CortexSession NewSession() => new CortexSession();

        [Fact]
        public void GetStatus_MetadataIsReadOnlyAndStatic()
        {
            var t = new DynamoGetStatusTool();
            Assert.Equal("dynamo_get_status", t.Name);
            Assert.True(t.IsDynamic);       // only visible when Dynamo present
            Assert.False(t.RequiresDocument);
        }

        [Fact]
        public void ListGraphIo_IsStaticReadOnly()
        {
            var t = new DynamoListGraphIoTool();
            Assert.Equal("dynamo_list_graph_io", t.Name);
            Assert.False(t.IsDynamic);      // static — parses JSON, never touches Dynamo
        }

        [Fact]
        public void ListGraphIo_ReturnsIoForRealDyn()
        {
            var dyn = new DynamoGraphBuilder().BuildDynJson(new DynamoGraphSpec(
                "T", "OUT = IN[0]",
                new List<GraphPort> { new GraphPort("folder", "String") },
                new List<GraphPort> { new GraphPort("result", "String") }));
            var path = Path.Combine(Path.GetTempPath(), "rc_io_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            File.WriteAllText(path, dyn);
            try
            {
                var t = new DynamoListGraphIoTool();
                var res = t.Execute(new JObject { ["dynPath"] = path }, NewSession());
                Assert.True(res.Success);
                var data = JObject.FromObject(res.Data!);
                Assert.Equal("T", (string)data["name"]);
                Assert.Equal(1, ((JArray)data["inputs"]).Count);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ListGraphIo_FailsOnMissingFile()
        {
            var t = new DynamoListGraphIoTool();
            var res = t.Execute(new JObject { ["dynPath"] = @"C:\does\not\exist.dyn" }, NewSession());
            Assert.False(res.Success);
        }
    }
}
```

Note: verify `new CortexSession()` has a usable parameterless constructor; if not, adapt to the actual construction used elsewhere in the test suite (search existing tests under `src/RevitCortex.Tests/` for how `CortexSession` is built).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoReadToolsTests"`
Expected: FAIL — tool types not defined.

- [ ] **Step 3: Implement the two read tools**

`Tools/DynamoGetStatusTool.cs`:

```csharp
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Runtime;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>Reports Dynamo presence/version/engine and whether the feature is enabled.</summary>
    public sealed class DynamoGetStatusTool : ICortexTool
    {
        public string Name => "dynamo_get_status";
        public string Category => "Dynamo";
        public bool RequiresDocument => false;
        public bool IsDynamic => true;
        public string Description => "Report Dynamo for Revit status (present, version, CPython3 availability) and whether EnableDynamo is set. Read-only diagnostic.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            int year = input["revitYear"]?.Value<int>() ?? DetectYear(session);
            var caps = new DynamoCapabilityProbe().Probe(year);
            var settings = CortexSettings.Load();
            return CortexResult<object>.Ok(new
            {
                enableDynamo = settings.EnableDynamo,
                isPresent = caps.IsPresent,
                dynamoVersion = caps.DynamoVersion,
                cpython3Expected = caps.CPython3Expected,
                dynamoForRevitDir = caps.DynamoForRevitDir,
                revitYear = year
            });
        }

        private static int DetectYear(CortexSession session)
        {
            // Best-effort: read from capabilities if present, else default to a modern year.
            return 2025;
        }
    }
}
```

`Tools/DynamoListGraphIoTool.cs`:

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Building;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>Parses a .dyn file (JSON only, never loads Dynamo) and returns its I/O interface.</summary>
    public sealed class DynamoListGraphIoTool : ICortexTool
    {
        public string Name => "dynamo_list_graph_io";
        public string Category => "Dynamo";
        public bool RequiresDocument => false;
        public bool IsDynamic => false; // static: pure JSON parse
        public string Description => "List the inputs and outputs of a .dyn Dynamo graph (parses the file, does not run it). Use before dynamo_run_graph to know which inputValues to pass.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var path = input["dynPath"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "dynPath is required");
            if (!File.Exists(path))
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"File not found: {path}");

            try
            {
                var info = DynGraphReader.Read(File.ReadAllText(path!));
                return CortexResult<object>.Ok(new
                {
                    name = info.Name,
                    dynamoVersion = info.DynamoVersion,
                    pythonEngine = info.PythonEngine,
                    pythonNodeCount = info.PythonNodeCount,
                    totalNodes = info.TotalNodes,
                    inputs = info.Inputs,
                    outputs = info.Outputs,
                    warnings = info.Warnings
                });
            }
            catch (System.Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Could not parse .dyn: {ex.Message}",
                    suggestion: "Ensure the file is a valid Dynamo 2.x/3.x JSON graph.");
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoReadToolsTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Tools/DynamoGetStatusTool.cs src/RevitCortex.Tools.Dynamo/Tools/DynamoListGraphIoTool.cs src/RevitCortex.Tests/Dynamo/DynamoReadToolsTests.cs
git commit -m "feat(dynamo): dynamo_get_status and dynamo_list_graph_io tools"
```

---

## Task 11: `dynamo_generate_graph` tool (TDD)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Tools/DynamoGenerateGraphTool.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoGenerateGraphToolTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/RevitCortex.Tests/Dynamo/DynamoGenerateGraphToolTests.cs`:

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Dynamo.Building;
using RevitCortex.Tools.Dynamo.Tools;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoGenerateGraphToolTests
    {
        private static CortexSession NewSession() => new CortexSession();

        private static string TempSettings(bool enableDynamo)
        {
            var path = Path.Combine(Path.GetTempPath(), "rc_gen_settings_" + System.Guid.NewGuid().ToString("N") + ".json");
            new CortexSettings { EnableDynamo = enableDynamo }.Save(path);
            return path;
        }

        [Fact]
        public void Generate_IsStaticWriteTool()
        {
            var t = new DynamoGenerateGraphTool();
            Assert.Equal("dynamo_generate_graph", t.Name);
            Assert.False(t.IsDynamic); // static — does not touch Dynamo
        }

        [Fact]
        public void Generate_RefusedWhenEnableDynamoFalse()
        {
            var t = new DynamoGenerateGraphTool { SettingsPathForTests = TempSettings(false) };
            var res = t.Execute(new JObject
            {
                ["name"] = "G",
                ["pythonCode"] = "OUT = 1",
                ["outputs"] = new JArray { new JObject { ["name"] = "result" } }
            }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
        }

        [Fact]
        public void Generate_BlocksUnsafePython()
        {
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            var res = t.Execute(new JObject
            {
                ["name"] = "G",
                ["pythonCode"] = "import System.IO\nSystem.IO.File.Delete('x')"
            }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
        }

        [Fact]
        public void Generate_WritesValidDynToGivenPath()
        {
            var outPath = Path.Combine(Path.GetTempPath(), "rc_gen_" + System.Guid.NewGuid().ToString("N") + ".dyn");
            var t = new DynamoGenerateGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            try
            {
                var res = t.Execute(new JObject
                {
                    ["name"] = "ExportRooms",
                    ["pythonCode"] = "OUT = IN[0]",
                    ["inputs"] = new JArray { new JObject { ["name"] = "folder", ["type"] = "String" } },
                    ["outputs"] = new JArray { new JObject { ["name"] = "result" } },
                    ["savePath"] = outPath,
                    ["execute"] = false
                }, NewSession());
                Assert.True(res.Success);
                Assert.True(File.Exists(outPath));
                var info = DynGraphReader.Read(File.ReadAllText(outPath));
                Assert.Equal("ExportRooms", info.Name);
                Assert.Equal("CPython3", info.PythonEngine);
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoGenerateGraphToolTests"`
Expected: FAIL — `DynamoGenerateGraphTool` not defined.

- [ ] **Step 3: Implement the tool**

Create `src/RevitCortex.Tools.Dynamo/Tools/DynamoGenerateGraphTool.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Building;
using RevitCortex.Tools.Dynamo.Security;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>
    /// Generates and saves a valid Python-centric .dyn. Static: never loads Dynamo.
    /// Gated by EnableDynamo + PythonSandbox + confirmation, mirroring send_code_to_revit.
    /// If execute=true, delegates to dynamo_run_graph after saving.
    /// </summary>
    public sealed class DynamoGenerateGraphTool : ICortexTool
    {
        public static readonly string DefaultGraphsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".revitcortex", "dynamo-graphs");

        // Test seams (default to production behavior).
        public string? SettingsPathForTests { get; set; }
        public bool SkipConfirmationForTests { get; set; }

        public string Name => "dynamo_generate_graph";
        public string Category => "Dynamo";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "Generate and save a valid Python-centric Dynamo .dyn graph from a Python body + typed inputs/outputs. Use ONLY when no native RevitCortex tool covers the task AND the user explicitly approved a Dynamo/Python approach. REQUIRES EnableDynamo=true in ~/.revitcortex/settings.json.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var settings = CortexSettings.Load(SettingsPathForTests);
            if (!settings.EnableDynamo)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "dynamo_generate_graph is disabled in this installation. STOP: do NOT retry this tool. Ask the user to enable Dynamo via Settings > Tools (or \"EnableDynamo\": true in ~/.revitcortex/settings.json), or solve the task with native tools.",
                    suggestion: "Do not retry. Either ask the user to enable Dynamo in Settings, or use native RevitCortex tools.");

            var pythonCode = input["pythonCode"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(pythonCode))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "pythonCode is required");

            var name = SanitizeName(input["name"]?.Value<string>() ?? "RevitCortexGraph");
            var inputs = ParsePorts(input["inputs"] as JArray);
            var outputs = ParsePorts(input["outputs"] as JArray);
            var engine = input["engine"]?.Value<string>() ?? "CPython3";

            var spec = new DynamoGraphSpec(name, pythonCode!, inputs, outputs, engine);

            var builder = new DynamoGraphBuilder();
            var validation = builder.ValidateSpec(spec);
            if (!validation.IsValid)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Invalid graph spec: " + string.Join("; ", validation.Errors));

            var sandbox = PythonSandbox.Validate(pythonCode!);
            if (sandbox != null) return sandbox;

            if (!SkipConfirmationForTests && !session.RequestConfirmation("generate Dynamo graph", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            string savePath = input["savePath"]?.Value<string>() ?? DefaultSavePath(name);
            try
            {
                var dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = builder.BuildDynJson(spec);
                File.WriteAllText(savePath, json);

                var result = JObject.FromObject(new
                {
                    savedTo = savePath,
                    name = spec.Name,
                    engine = spec.Engine,
                    inputCount = spec.Inputs.Count,
                    outputCount = spec.Outputs.Count,
                    bytes = new FileInfo(savePath).Length
                });

                bool execute = input["execute"]?.Value<bool>() ?? false;
                result["executeRequested"] = execute;
                // Actual headless run is delegated to dynamo_run_graph by the router/caller;
                // this tool never loads Dynamo. When execute=true the caller should invoke
                // dynamo_run_graph with { dynPath: savedTo }.
                return CortexResult<object>.Ok(result);
            }
            catch (Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to write .dyn: {ex.Message}");
            }
        }

        private static List<GraphPort> ParsePorts(JArray? arr)
        {
            var list = new List<GraphPort>();
            if (arr == null) return list;
            foreach (var e in arr)
            {
                var n = e["name"]?.Value<string>() ?? "";
                var t = e["type"]?.Value<string>() ?? "String";
                list.Add(new GraphPort(n, t));
            }
            return list;
        }

        private static string DefaultSavePath(string name)
            => UniquePath(Path.Combine(DefaultGraphsFolder, name + ".dyn"));

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        private static string SanitizeName(string name)
        {
            var safe = Regex.Replace(name, @"[^\w\-]", "-").Trim('-');
            if (safe.Length == 0) safe = "RevitCortexGraph";
            return safe.Substring(0, Math.Min(safe.Length, 60));
        }
    }
}
```

Note on `execute`: to keep this tool strictly static (never loading Dynamo), it does NOT call the runtime itself. The server wrapper (Task 13) chains `dynamo_run_graph` when `execute=true`. This keeps the "generation never fails because of Dynamo" guarantee intact.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoGenerateGraphToolTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Tools/DynamoGenerateGraphTool.cs src/RevitCortex.Tests/Dynamo/DynamoGenerateGraphToolTests.cs
git commit -m "feat(dynamo): dynamo_generate_graph tool (gated + sandboxed)"
```

---

## Task 12: `DynamoRuntimeLoader` + `dynamo_run_graph` tool (reflection late-binding)

**Files:**
- Create: `src/RevitCortex.Tools.Dynamo/Runtime/DynamoRuntimeLoader.cs`
- Create: `src/RevitCortex.Tools.Dynamo/Tools/DynamoRunGraphTool.cs`
- Test: `src/RevitCortex.Tests/Dynamo/DynamoRunGraphToolGateTests.cs`

Note: the actual headless execution cannot be unit-tested without Revit+Dynamo (verified live in Task 16). These tests cover ONLY the gates and pre-flight checks that run before any Dynamo DLL is touched.

- [ ] **Step 1: Write the failing test (gates only)**

Create `src/RevitCortex.Tests/Dynamo/DynamoRunGraphToolGateTests.cs`:

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Dynamo.Tools;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    public class DynamoRunGraphToolGateTests
    {
        private static CortexSession NewSession() => new CortexSession();

        private static string TempSettings(bool enableDynamo)
        {
            var path = Path.Combine(Path.GetTempPath(), "rc_run_settings_" + System.Guid.NewGuid().ToString("N") + ".json");
            new CortexSettings { EnableDynamo = enableDynamo }.Save(path);
            return path;
        }

        [Fact]
        public void Run_MetadataIsDynamicWrite()
        {
            var t = new DynamoRunGraphTool();
            Assert.Equal("dynamo_run_graph", t.Name);
            Assert.True(t.IsDynamic);  // requires Dynamo present
        }

        [Fact]
        public void Run_RefusedWhenEnableDynamoFalse()
        {
            var t = new DynamoRunGraphTool { SettingsPathForTests = TempSettings(false) };
            var res = t.Execute(new JObject { ["dynPath"] = @"C:\x.dyn" }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.PermissionDenied, res.Error!.Code);
        }

        [Fact]
        public void Run_FailsCleanWhenFileMissing_AfterEnable()
        {
            var t = new DynamoRunGraphTool
            {
                SettingsPathForTests = TempSettings(true),
                SkipConfirmationForTests = true
            };
            var res = t.Execute(new JObject { ["dynPath"] = @"C:\does\not\exist.dyn" }, NewSession());
            Assert.False(res.Success);
            Assert.Equal(CortexErrorCode.ElementNotFound, res.Error!.Code);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoRunGraphToolGateTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement DynamoRuntimeLoader (reflection, defensive)**

Create `src/RevitCortex.Tools.Dynamo/Runtime/DynamoRuntimeLoader.cs`:

```csharp
using System;
using System.IO;
using System.Reflection;

namespace RevitCortex.Tools.Dynamo.Runtime
{
    /// <summary>
    /// Lazily loads Dynamo assemblies via reflection from the Revit install path.
    /// No compile-time dependency on Dynamo. Any failure is returned to the caller,
    /// never allowed to propagate to plugin load.
    /// </summary>
    public sealed class DynamoRuntimeLoader
    {
        private readonly string _dynamoForRevitDir;
        private bool _resolverHooked;

        public DynamoRuntimeLoader(string dynamoForRevitDir)
        {
            _dynamoForRevitDir = dynamoForRevitDir;
        }

        /// <summary>Returns null on success, or an error message string on failure.</summary>
        public string? EnsureLoaded()
        {
            try
            {
                if (!Directory.Exists(_dynamoForRevitDir))
                    return $"Dynamo for Revit folder not found: {_dynamoForRevitDir}";

                HookResolver();

                var revitDs = Path.Combine(_dynamoForRevitDir, "Revit", "DynamoRevitDS.dll");
                if (!File.Exists(revitDs))
                    revitDs = Path.Combine(_dynamoForRevitDir, "DynamoRevitDS.dll");
                if (!File.Exists(revitDs))
                    return $"DynamoRevitDS.dll not found under {_dynamoForRevitDir}";

                Assembly.LoadFrom(revitDs);
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to load Dynamo runtime: {ex.Message}";
            }
        }

        private void HookResolver()
        {
            if (_resolverHooked) return;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDynamoDir;
            _resolverHooked = true;
        }

        private Assembly? ResolveFromDynamoDir(object? sender, ResolveEventArgs args)
        {
            try
            {
                var simpleName = new AssemblyName(args.Name).Name + ".dll";
                foreach (var root in new[] { _dynamoForRevitDir, Path.Combine(_dynamoForRevitDir, "Revit") })
                {
                    var candidate = Path.Combine(root, simpleName);
                    if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                }
            }
            catch { }
            return null;
        }
    }
}
```

- [ ] **Step 4: Implement DynamoRunGraphTool (gates + reflection execution)**

Create `src/RevitCortex.Tools.Dynamo/Tools/DynamoRunGraphTool.cs`:

```csharp
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Runtime;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>
    /// Runs a .dyn headless inside Revit via reflection (journal-based launch).
    /// Dynamic: only registered when Dynamo is present. All Dynamo access is reflection —
    /// a load failure returns a structured error, never crashes the plugin.
    /// </summary>
    public sealed class DynamoRunGraphTool : ICortexTool
    {
        public string? SettingsPathForTests { get; set; }
        public bool SkipConfirmationForTests { get; set; }

        public string Name => "dynamo_run_graph";
        public string Category => "Dynamo";
        public bool RequiresDocument => true;
        public bool IsDynamic => true;
        public string Description => "Run a Dynamo .dyn graph headless inside Revit and return its output. Use ONLY when no native RevitCortex tool covers the task AND the user explicitly approved a Dynamo/Python approach. REQUIRES EnableDynamo=true in ~/.revitcortex/settings.json.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var settings = CortexSettings.Load(SettingsPathForTests);
            if (!settings.EnableDynamo)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "dynamo_run_graph is disabled in this installation. STOP: do NOT retry this tool. Ask the user to enable Dynamo via Settings > Tools (or \"EnableDynamo\": true in ~/.revitcortex/settings.json).",
                    suggestion: "Do not retry. Ask the user to enable Dynamo, or use native tools.");

            var path = input["dynPath"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "dynPath is required");
            if (!File.Exists(path))
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"File not found: {path}");

            if (!SkipConfirmationForTests && !session.RequestConfirmation("run Dynamo graph", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            int year = input["revitYear"]?.Value<int>() ?? 2025;
            var caps = new DynamoCapabilityProbe().Probe(year);
            if (!caps.IsPresent)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Dynamo for Revit is not installed for this Revit version.",
                    suggestion: "Open the .dyn manually in Dynamo, or install Dynamo for Revit.");

            var loader = new DynamoRuntimeLoader(caps.DynamoForRevitDir);
            var loadError = loader.EnsureLoaded();
            if (loadError != null)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    loadError,
                    suggestion: "The .dyn is still valid — open it manually in Dynamo. Check the Dynamo version compatibility.");

            // Headless execution via reflection (journal-based) is performed here.
            // This runs on Revit's main thread (guaranteed by RevitThreadDispatcher).
            // Implemented and verified live in Task 16; see the spec §5.4 for the
            // JournalKeys + DynamoRevit.ExecuteCommand + ForceRun sequence.
            return RunHeadless(input, session, caps);
        }

        private CortexResult<object> RunHeadless(JObject input, CortexSession session, DynamoCapabilities caps)
        {
            // Placeholder returning a clear "not yet wired" structured result until Task 16.
            // Task 16 replaces this body with the reflection-driven journal launch.
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                "Headless run not yet wired (pending live integration, Task 16).",
                suggestion: "Open the generated .dyn manually in Dynamo for now.");
        }
    }
}
```

- [ ] **Step 5: Run to verify gate tests pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DynamoRunGraphToolGateTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Build R24 (net48) to confirm reflection code compiles cross-target**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj`
Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add src/RevitCortex.Tools.Dynamo/Runtime/DynamoRuntimeLoader.cs src/RevitCortex.Tools.Dynamo/Tools/DynamoRunGraphTool.cs src/RevitCortex.Tests/Dynamo/DynamoRunGraphToolGateTests.cs
git commit -m "feat(dynamo): dynamo_run_graph gates + reflection loader (headless body deferred to Task 16)"
```

---

## Task 13: Register the Tools.Dynamo assembly + server wrappers

**Files:**
- Modify: `src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs`
- Create: `src/RevitCortex.Server/Tools/DynamoTools.cs`

- [ ] **Step 1: Add a ProjectReference so the DLL is copied to the plugin output**

In `src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`, inside the ItemGroup at lines 76-78, add (this is for build ordering + output copy; the plugin still discovers tools via reflection):

```xml
    <ProjectReference Include="..\RevitCortex.Tools.Dynamo\RevitCortex.Tools.Dynamo.csproj" />
```

- [ ] **Step 2: Load the new assembly at startup**

In `src/RevitCortex.Plugin/RevitCortexApp.cs`, immediately after the existing block that registers `RevitCortex.Tools.dll` (around lines 90-94), add:

```csharp
            var dynamoToolsAssembly = LoadNamedToolsAssembly("RevitCortex.Tools.Dynamo.dll");
            if (dynamoToolsAssembly != null)
            {
                _router.RegisterToolsFromAssembly(dynamoToolsAssembly);
            }
```

- [ ] **Step 3: Add the generalized loader helper**

In `src/RevitCortex.Plugin/RevitCortexApp.cs`, next to `LoadToolsAssembly()` (lines 614-629), add:

```csharp
        private Assembly? LoadNamedToolsAssembly(string dllName)
        {
            try
            {
                var pluginDir = System.IO.Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!;
                var path = System.IO.Path.Combine(pluginDir, dllName);
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Could not load {dllName}: {ex.Message}");
                return null;
            }
        }
```

- [ ] **Step 4: Create the server wrappers**

Create `src/RevitCortex.Server/Tools/DynamoTools.cs`:

```csharp
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class DynamoTools
{
    [McpServerTool(Name = "dynamo_get_status"),
     Description("Report Dynamo for Revit status (present, version, CPython3 availability) and whether EnableDynamo is set.")]
    public static async Task<string> DynamoGetStatus(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("dynamo_get_status", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "dynamo_list_graph_io"),
     Description("List inputs/outputs of a .dyn graph (parses the file, does not run it).")]
    public static async Task<string> DynamoListGraphIo(
        RevitConnectionManager revit,
        [Description("Absolute path to the .dyn file")] string dynPath,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dynPath"] = dynPath };
        var result = await revit.ExecuteAsync("dynamo_list_graph_io", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "dynamo_generate_graph"),
     Description("Generate and save a valid Python-centric Dynamo .dyn graph. Use ONLY when no native tool covers the task AND the user approved a Dynamo/Python approach. Requires EnableDynamo=true.")]
    public static async Task<string> DynamoGenerateGraph(
        RevitConnectionManager revit,
        [Description("Graph name (used for the default file name)")] string name,
        [Description("Python body; inputs arrive as list IN, output assigned to OUT")] string pythonCode,
        [Description("JSON array of inputs: [{\"name\":\"folder\",\"type\":\"String\"}]")] string? inputs = null,
        [Description("JSON array of outputs: [{\"name\":\"result\"}]")] string? outputs = null,
        [Description("Optional absolute save path; defaults to ~/.revitcortex/dynamo-graphs/")] string? savePath = null,
        [Description("If true, run the graph headless after saving")] bool execute = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["name"] = name,
            ["pythonCode"] = pythonCode,
            ["execute"] = execute
        };
        if (!string.IsNullOrEmpty(inputs)) p["inputs"] = JArray.Parse(inputs);
        if (!string.IsNullOrEmpty(outputs)) p["outputs"] = JArray.Parse(outputs);
        if (!string.IsNullOrEmpty(savePath)) p["savePath"] = savePath;

        var gen = await revit.ExecuteAsync("dynamo_generate_graph", p, ct);

        if (execute && gen["success"]?.Value<bool>() != false)
        {
            var savedTo = gen["savedTo"]?.ToString() ?? gen["data"]?["savedTo"]?.ToString();
            if (!string.IsNullOrEmpty(savedTo))
            {
                var runP = new JObject { ["dynPath"] = savedTo };
                var run = await revit.ExecuteAsync("dynamo_run_graph", runP, ct);
                return new JObject { ["generate"] = gen, ["run"] = run }.ToString();
            }
        }
        return gen.ToString();
    }

    [McpServerTool(Name = "dynamo_run_graph"),
     Description("Run a Dynamo .dyn graph headless inside Revit. Use ONLY when no native tool covers the task AND the user approved a Dynamo/Python approach. Requires EnableDynamo=true.")]
    public static async Task<string> DynamoRunGraph(
        RevitConnectionManager revit,
        [Description("Absolute path to the .dyn file")] string dynPath,
        [Description("Optional JSON object of input values: {\"folder\":\"C:/out\"}")] string? inputValues = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dynPath"] = dynPath };
        if (!string.IsNullOrEmpty(inputValues)) p["inputValues"] = JObject.Parse(inputValues);
        var result = await revit.ExecuteAsync("dynamo_run_graph", p, ct);
        return result.ToString();
    }
}
```

Note: the `execute=true` chaining is done here (server side), keeping `dynamo_generate_graph` itself Dynamo-free. Verify the shape of the generate result (`savedTo` at root vs under `data`) against Task 11's actual output and adjust the `savedTo` lookup accordingly.

- [ ] **Step 5: Build plugin (R25) and server**

Run:
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```
Expected: both `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Plugin/RevitCortex.Plugin.csproj src/RevitCortex.Plugin/RevitCortexApp.cs src/RevitCortex.Server/Tools/DynamoTools.cs
git commit -m "feat(dynamo): register Tools.Dynamo assembly + MCP server wrappers"
```

---

## Task 14: Settings UI checkbox + docs

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml`
- Modify: `src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml.cs`
- Modify: `CLAUDE.md`
- Modify: `USER_GUIDE.md`
- Modify: `tool-schemas.txt` (regenerate)

- [ ] **Step 1: Read the current ToolsSettingsPage to match its pattern**

Run: open `src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml` and `.xaml.cs`. Find how the `EnableCodeExecution` checkbox is declared and bound (search for "CodeExecution" or "Enable"). Mirror that exact pattern for a new "Enable Dynamo integration" checkbox bound to `CortexSettings.EnableDynamo` in the same load/save flow. Because the exact XAML/handler names depend on the current file, replicate the existing checkbox block verbatim with the property swapped to `EnableDynamo` and label "Enable Dynamo integration (dynamo_generate_graph / dynamo_run_graph)".

- [ ] **Step 2: Verify the settings save round-trips both flags**

The `GeneralSettingsPage` merge-write already preserves keys from other pages (GeneralSettingsPage.xaml.cs:385). Confirm `ToolsSettingsPage` save writes `EnableDynamo` without dropping `EnableCodeExecution` (and vice versa). If ToolsSettingsPage does a full-object save, ensure it loads current settings first, sets the two flags, and saves — matching the existing code-execution flow.

- [ ] **Step 3: Add the routing rule to CLAUDE.md**

In `CLAUDE.md`, under the `send_code_to_revit` section (search "send_code_to_revit"), add a sibling subsection:

```markdown
### Dynamo tools (dynamo_generate_graph / dynamo_run_graph)

Escape-hatch to the full Revit API via a CPython3 Python node in a generated .dyn.
**RevitCortex native tools ALWAYS have priority.** Use dynamo_* ONLY when:
1. No native RevitCortex tool covers the task, AND
2. The user explicitly approved a Dynamo/Python approach.

Both write tools REQUIRE `EnableDynamo=true` in `~/.revitcortex/settings.json` and show a
confirmation dialog. `dynamo_generate_graph` and `dynamo_list_graph_io` never load Dynamo;
`dynamo_run_graph` requires Dynamo for Revit installed. Default output folder:
`~/.revitcortex/dynamo-graphs/`. Python is sandboxed (no System.IO/Net/Process) on the
automated channel — a user can still open the .dyn by hand.
```

- [ ] **Step 4: Add a Dynamo section to USER_GUIDE.md**

In `USER_GUIDE.md`, add a "Dynamo" discipline section documenting the 4 tools with one natural-language prompt example each (mirror the format of existing sections). Example entry:

```markdown
## Dynamo

- **dynamo_get_status** — "Is Dynamo available and enabled?"
- **dynamo_list_graph_io** — "What inputs does C:/graphs/export.dyn expect?"
- **dynamo_generate_graph** — "Generate a Dynamo graph that renames all sheets by a prefix (Python)."
- **dynamo_run_graph** — "Run C:/graphs/export.dyn headless."

Requires enabling Dynamo integration in Settings > Tools (EnableDynamo).
```

- [ ] **Step 5: Regenerate tool-schemas.txt**

Run: `node server/generate-tool-schemas-csharp.mjs`
Expected: `tool-schemas.txt` now contains the 4 `dynamo_*` signatures. If the generator reads from the server tool attributes, confirm the 4 appear; if it errors, inspect and fix per the generator's expectations.

- [ ] **Step 6: Build plugin R25 to confirm the XAML change compiles**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml.cs CLAUDE.md USER_GUIDE.md tool-schemas.txt
git commit -m "feat(dynamo): settings UI toggle + docs (routing rule, user guide, schemas)"
```

---

## Task 15 (Phase B): Cross-target build parity R23/R24/R26/R27 + full test run

**Files:** none (build/test verification only)

- [ ] **Step 1: Build all five targets for the new project**

Run each:
```bash
dotnet build -c "Debug R23" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
dotnet build -c "Debug R25" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
dotnet build -c "Debug R27" src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj
```
Expected: all `Build succeeded`. R27 requires .NET SDK ≥10; if only SDK 8 is present it fails with NETSDK1045 — note it and continue (per CLAUDE.md, `global.json` rollForward accepts a newer SDK when present).

- [ ] **Step 2: Build the plugin for R23 and R24 (net48) to catch net48-only breaks**

Run:
```bash
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Expected: `Build succeeded`. Fix any net48 issues (no record/init/Index/GetValueOrDefault) in Tools.Dynamo before proceeding.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
Expected: all existing tests + new Dynamo tests pass; RevitAPIUI-dependent tests skip (per CLAUDE.md: expect e.g. "N passed / 1 skipped / 0 failed", now with the added Dynamo tests in the passed count).

- [ ] **Step 4: Commit (if any fixes were needed)**

```bash
git add -A
git commit -m "fix(dynamo): cross-target (net48/net10) build parity"
```

---

## Task 16 (Phase C): Live headless-run integration + manual verification

**Files:**
- Modify: `src/RevitCortex.Tools.Dynamo/Tools/DynamoRunGraphTool.cs` (replace `RunHeadless` body)

This task requires a machine with Revit 2025 + Dynamo for Revit installed. It replaces the deferred `RunHeadless` placeholder with the reflection-driven journal launch and verifies it live. Because it cannot be unit-tested without Revit, verification is manual (documented steps).

- [ ] **Step 1: Implement the reflection-driven journal launch**

Replace the `RunHeadless` method body in `DynamoRunGraphTool.cs` with the reflection sequence per spec §5.4:
- Get the `UIApplication` from `session.Store.Get<object>("uiApplication")`.
- Via reflection on the loaded `DynamoRevitDS`: build `JournalKeys` dictionary (`ShowUiKey=false`, `AutomationModeKey=true`, `DynPathKey=dynPath`, `DynPathExecuteKey=true`, `ForceManualRunKey=false`), construct `DynamoRevitCommandData` with `Application` + `JournalData`, instantiate `DynamoRevit`, call `ExecuteCommand(cmdData)`, then `DynamoRevit.RevitDynamoModel.ForceRun()`.
- Collect Watch/output-node results if accessible; otherwise return success with a note that outputs are visible in the generated graph.
- Wrap everything in try/catch → `CortexResult.Fail(TransactionFailed, ...)` with the "open manually" suggestion on failure.

(Exact reflection member names must be confirmed against the installed `DynamoRevitDS.dll` — use the probe's `DynamoForRevitDir` to locate it; verify types `Dynamo.Applications.DynamoRevit`, `Dynamo.Applications.DynamoRevitCommandData`, `Dynamo.Applications.JournalKeys`.)

- [ ] **Step 2: Build and deploy to Revit 2025**

Run:
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
powershell -ExecutionPolicy Bypass -File deploy.ps1
```
Restart Revit 2025.

- [ ] **Step 3: Manual verification checklist**

- Enable Dynamo in Settings > Tools (or set `"EnableDynamo": true`).
- Open a Revit model.
- Via MCP/bridge: call `dynamo_get_status` → expect `isPresent:true`, a version string, `enableDynamo:true`.
- Call `dynamo_generate_graph` with a trivial Python body (`OUT = "hello from " + str(len(IN))`) and one String input → expect a saved `.dyn` in `~/.revitcortex/dynamo-graphs/`.
- Open that `.dyn` manually in Dynamo → expect it opens with NO red/missing nodes, one Python node (CPython3), one input, one Watch.
- Call `dynamo_run_graph` on it → expect success and the Watch output returned.
- With `EnableDynamo=false` → both write tools refuse with PermissionDenied.
- Confirm the other 288 tools still work (spot check `get_project_info`).

- [ ] **Step 4: Record results and commit**

Document the verification outcome (versions tested, pass/fail per step) in the commit body.

```bash
git add src/RevitCortex.Tools.Dynamo/Tools/DynamoRunGraphTool.cs
git commit -m "feat(dynamo): wire headless run via reflection + live verification on R25"
```

---

## Definition of Done (branch stable → mergeable)

- [ ] All 5 targets (`Debug R23`…`Debug R27`) build for Tools.Dynamo and Plugin (R27 subject to SDK 10 availability).
- [ ] Full test suite green on Debug R25 (new Dynamo unit tests included; RevitAPIUI tests skip cleanly).
- [ ] `dynamo_generate_graph` + `dynamo_list_graph_io` verified producing/parsing a valid .dyn.
- [ ] Generated .dyn opens in Dynamo with no missing nodes (manual, Task 16).
- [ ] `dynamo_run_graph` verified end-to-end on R25 (manual, Task 16).
- [ ] With Dynamo absent: 288 tools + generate + list still work; run + status auto-disable cleanly.
- [ ] With `EnableDynamo=false`: write tools refuse with the correct message.
- [ ] Docs updated: USER_GUIDE.md, tool-schemas.txt, CLAUDE.md routing rule (per memory: update-docs-after-tool-change).
- [ ] Deploy verified on all installed targets (per memory: deploy-all-revit-targets).
