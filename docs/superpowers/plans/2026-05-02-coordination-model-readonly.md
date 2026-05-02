# Coordination Model Read-Only Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a safe read-only `get_coordination_models` RevitCortex tool that lists linked Coordination Models without changing the Revit document or active view.

**Architecture:** Implement one focused `ICortexTool` in `src/RevitCortex.Tools/LinkedFiles`. The tool compiles on every supported Revit target, returns a controlled unsupported payload on targets without Coordination Model API access, and uses SDK-derived `CoordinationModelLinkUtils` calls only inside preprocessor guards.

**Tech Stack:** C#, Revit API, Newtonsoft.Json `JObject`, xUnit, existing RevitCortex `ICortexTool` / `CortexResult<object>` conventions.

---

## File Structure

- Create `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs`
  - Owns the MCP tool surface, input parsing, unsupported-target response, R26+ Coordination Model discovery, and compact output shaping.
- Create `src/RevitCortex.Tests/LinkedFiles/GetCoordinationModelsToolTests.cs`
  - Tests pure helper behavior that does not require a live Revit `Document`.
- Modify `tool-schemas.txt`
  - Regenerated after adding the tool so compact tool signatures include `get_coordination_models`.
- No changes to `manage_links`; Coordination Models remain a separate surface from Revit/CAD/IFC links.

---

### Task 1: Add Pure Helper Tests

**Files:**
- Create: `src/RevitCortex.Tests/LinkedFiles/GetCoordinationModelsToolTests.cs`
- Later implementation file: `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs`

- [ ] **Step 1: Write failing tests for input normalization and name filtering**

Create `src/RevitCortex.Tests/LinkedFiles/GetCoordinationModelsToolTests.cs` with:

```csharp
using RevitCortex.Tools.LinkedFiles;
using Xunit;

namespace RevitCortex.Tests.LinkedFiles;

public class GetCoordinationModelsToolTests
{
    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(500, 250)]
    public void NormalizeMaxInstances_UsesDefaultAndCap(int? rawValue, int expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.NormalizeMaxInstances(rawValue));
    }

    [Theory]
    [InlineData(null, "Coordination A", true)]
    [InlineData("", "Coordination A", true)]
    [InlineData("coord", "Coordination A", true)]
    [InlineData("MODEL", "Coordination Model", true)]
    [InlineData("navis", "Coordination Model", false)]
    public void MatchesNameFilter_IsCaseInsensitive(string? filter, string candidate, bool expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.MatchesNameFilter(filter, candidate));
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
dotnet test src\RevitCortex.Tests\RevitCortex.Tests.csproj -c "Debug R26" --filter FullyQualifiedName~GetCoordinationModelsToolTests
```

Expected: build/test fails because `GetCoordinationModelsTool` does not exist yet.

- [ ] **Step 3: Commit the failing tests only if your workflow requires test checkpoints**

Preferred for this repo: do not commit the failing test checkpoint. Continue to Task 2 and commit once the implementation passes.

---

### Task 2: Implement `get_coordination_models`

**Files:**
- Create: `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs`
- Test: `src/RevitCortex.Tests/LinkedFiles/GetCoordinationModelsToolTests.cs`

- [ ] **Step 1: Add the tool implementation**

Create `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
#if REVIT2026_OR_GREATER
using Autodesk.Revit.DB.ExternalData;
#endif
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Lists linked Coordination Models in the active document without modifying model or view state.
/// </summary>
public class GetCoordinationModelsTool : ICortexTool
{
    private const int DefaultMaxInstances = 100;
    private const int MaxInstanceCap = 250;
    private const double MmPerFoot = 304.8;

    public string Name => "get_coordination_models";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists linked Coordination Models with compact type, path, and instance metadata. Read-only; no reloads or view changes.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput,
                "No active document in session",
                suggestion: "Open a Revit document before using this tool");
        }

        var nameFilter = input["nameFilter"]?.Value<string>();
        var includeInstances = input["includeInstances"]?.Value<bool>() ?? true;
        int? rawMaxInstances = input["maxInstances"]?.Value<int?>();

        if (rawMaxInstances.HasValue && rawMaxInstances.Value < 0)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput,
                "maxInstances must be greater than or equal to 0");
        }

        var maxInstances = NormalizeMaxInstances(rawMaxInstances);

        try
        {
#if REVIT2026_OR_GREATER
            return ListCoordinationModels(doc, nameFilter, includeInstances, maxInstances);
#else
            return Unsupported();
#endif
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.Unknown,
                $"Failed to list coordination models: {ex.Message}");
        }
    }

    public static int NormalizeMaxInstances(int? rawValue)
    {
        if (!rawValue.HasValue)
            return DefaultMaxInstances;

        if (rawValue.Value > MaxInstanceCap)
            return MaxInstanceCap;

        return rawValue.Value;
    }

    public static bool MatchesNameFilter(string? filter, string candidate)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return candidate.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static CortexResult<object> Unsupported()
    {
        return CortexResult<object>.Ok(new
        {
            apiAvailable = false,
            modelCount = 0,
            totalInstances = 0,
            models = Array.Empty<object>(),
            message = "Coordination Model API is not available for this Revit target."
        });
    }

#if REVIT2026_OR_GREATER
    private static CortexResult<object> ListCoordinationModels(
        Document doc,
        string? nameFilter,
        bool includeInstances,
        int maxInstances)
    {
        var typeIds = CoordinationModelLinkUtils.GetAllCoordinationModelTypeIds(doc).ToList();
        var instanceIds = CoordinationModelLinkUtils.GetAllCoordinationModelInstanceIds(doc).ToList();
        var instancesByType = new Dictionary<ElementId, List<Element>>();

        foreach (var instanceId in instanceIds)
        {
            var instance = doc.GetElement(instanceId);
            if (instance == null)
                continue;

            var typeId = instance.GetTypeId();
            List<Element>? bucket;
            if (!instancesByType.TryGetValue(typeId, out bucket))
            {
                bucket = new List<Element>();
                instancesByType[typeId] = bucket;
            }

            bucket.Add(instance);
        }

        var models = new List<object>();
        var includedInstanceCount = 0;

        foreach (var typeId in typeIds)
        {
            var type = doc.GetElement(typeId) as ElementType;
            if (type == null)
                continue;

            if (!MatchesNameFilter(nameFilter, type.Name))
                continue;

            List<Element>? typeInstances;
            if (!instancesByType.TryGetValue(typeId, out typeInstances))
                typeInstances = new List<Element>();

            var instancePayload = new List<object>();
            if (includeInstances && maxInstances > 0)
            {
                foreach (var instance in typeInstances.Take(maxInstances))
                {
                    instancePayload.Add(BuildInstancePayload(instance));
                }
            }

            includedInstanceCount += instancePayload.Count;

            var data = CoordinationModelLinkUtils.GetCoordinationModelTypeData(doc, type);
            var pathType = data != null ? data.GetPathType().ToString() : "";

            models.Add(new
            {
                typeId = ToolHelpers.GetElementIdValue(type.Id),
                typeName = type.Name,
                pathType,
                isCloud = string.Equals(pathType, "Cloud", StringComparison.OrdinalIgnoreCase),
                path = TryReadStringMember(data, "GetPath")
                    ?? TryReadStringMember(data, "GetLocalPath")
                    ?? "",
                instanceCount = typeInstances.Count,
                instances = instancePayload
            });
        }

        return CortexResult<object>.Ok(new
        {
            apiAvailable = true,
            modelCount = models.Count,
            totalInstances = includedInstanceCount,
            models,
            message = models.Count == 0
                ? "No coordination models found in the active document."
                : $"Found {models.Count} coordination model type(s)."
        });
    }

    private static object BuildInstancePayload(Element instance)
    {
        return new
        {
            instanceId = ToolHelpers.GetElementIdValue(instance.Id),
            name = instance.Name,
            origin = TryReadTransformOriginMm(instance)
        };
    }

    private static object? TryReadTransformOriginMm(Element element)
    {
        var method = element.GetType().GetMethod("GetTotalTransform", Type.EmptyTypes)
            ?? element.GetType().GetMethod("GetTransform", Type.EmptyTypes);

        if (method == null)
            return null;

        var transform = method.Invoke(element, null) as Transform;
        if (transform == null)
            return null;

        return new
        {
            x = Math.Round(transform.Origin.X * MmPerFoot, 1),
            y = Math.Round(transform.Origin.Y * MmPerFoot, 1),
            z = Math.Round(transform.Origin.Z * MmPerFoot, 1)
        };
    }

    private static string? TryReadStringMember(object? target, string methodName)
    {
        if (target == null)
            return null;

        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (method == null)
            return null;

        var value = method.Invoke(target, null);
        return value?.ToString();
    }
#endif
}
```

- [ ] **Step 2: Run focused tests and verify helper behavior passes**

Run:

```powershell
dotnet test src\RevitCortex.Tests\RevitCortex.Tests.csproj -c "Debug R26" --filter FullyQualifiedName~GetCoordinationModelsToolTests
```

Expected: tests pass.

- [ ] **Step 3: Run tool registration tests**

Run:

```powershell
dotnet test src\RevitCortex.Tests\RevitCortex.Tests.csproj -c "Debug R26" --filter FullyQualifiedName~ToolRegistrationTests
```

Expected: tests pass, including unique snake_case tool names and instantiation.

- [ ] **Step 4: Commit implementation and tests**

Run:

```powershell
git add src\RevitCortex.Tools\LinkedFiles\GetCoordinationModelsTool.cs src\RevitCortex.Tests\LinkedFiles\GetCoordinationModelsToolTests.cs
git commit -m "feat: add read-only coordination model tool"
```

Expected: commit succeeds with only the new tool and tests.

---

### Task 3: Regenerate Tool Schema Signatures

**Files:**
- Modify: `tool-schemas.txt`

- [ ] **Step 1: Regenerate compact tool schema signatures**

Run:

```powershell
node server/generate-tool-schemas-csharp.mjs
```

Expected: command exits successfully and updates `tool-schemas.txt`.

- [ ] **Step 2: Verify the new signature is present**

Run:

```powershell
rg -n "^get_coordination_models" tool-schemas.txt
```

Expected: one line for `get_coordination_models` appears.

- [ ] **Step 3: Commit schema update**

Run:

```powershell
git add tool-schemas.txt
git commit -m "docs: add coordination model tool schema"
```

Expected: commit succeeds with only `tool-schemas.txt`.

---

### Task 4: Cross-Target Verification

**Files:**
- Verify: `src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
- Verify: `src/RevitCortex.Tools/RevitCortex.Tools.csproj`

- [ ] **Step 1: Build R25**

Run:

```powershell
dotnet build -c "Debug R25" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
```

Expected: build succeeds.

- [ ] **Step 2: Build R24**

Run:

```powershell
dotnet build -c "Debug R24" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
```

Expected: build succeeds and proves the new file avoids R26-only API references on `net48`.

- [ ] **Step 3: Build R26**

Run:

```powershell
dotnet build -c "Debug R26" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
```

Expected: build succeeds and proves the Coordination Model API branch compiles.

- [ ] **Step 4: Optionally build R27**

Run only if the current .NET SDK setup supports `net10.0-windows7.0`:

```powershell
dotnet build -c "Debug R27" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
```

Expected: build succeeds. If the build fails with SDK targeting errors, record the SDK limitation rather than changing feature code.

- [ ] **Step 5: Record verification outcome**

Update the final handoff with:

```text
Verified:
- dotnet test ... GetCoordinationModelsToolTests
- dotnet test ... ToolRegistrationTests
- dotnet build ... Debug R25
- dotnet build ... Debug R24
- dotnet build ... Debug R26

Not run:
- Debug R27, if SDK setup blocks net10
```

Do not update `WORKFLOWS.md` for this tool until a real-model smoke test confirms the output shape against an actual Coordination Model.

---

## Self-Review

Spec coverage:

- Read-only tool surface is covered by Task 2.
- Unsupported target response is covered by Task 2 with preprocessor guards.
- Cross-target builds are covered by Task 4.
- Tool schema regeneration is covered by Task 3.
- No workflow documentation is added until real-model verification, matching the spec rollout section.

Placeholder scan:

- The plan contains no TBD/TODO placeholders.
- Every code-changing task includes exact file paths, code content, commands, and expected outcomes.

Type consistency:

- The tool name is consistently `get_coordination_models`.
- The implementation file and tests consistently use `GetCoordinationModelsTool`.
- Helper names used in tests match the public static methods defined in Task 2.
