# IFC File Management — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement 10 MCP tools for IFC file management in RevitCortex — capability discovery, link, reload, open/import, export (basic & configured), configuration listing/reading, family mapping, and request validation.

**Architecture:** Each tool is a C# class implementing `ICortexTool` in a new `IFC/` folder under `RevitCortex.Tools`, with matching Zod schemas in `server/src/schemas/ifc.ts` and TypeScript registrations in `server/src/tools/ifc_*.ts`. IFC import uses `Autodesk.Revit.DB.IFC.IFCImportOptions`, export uses `Autodesk.Revit.DB.IFCExportOptions`, and linking uses `RevitLinkType.CreateFromIFC`. Extra options from the open-source revit-ifc add-in are passed via `IFCExportOptions.AddOption()`.

**Tech Stack:** C# (.NET 8 / .NET Framework 4.8 multi-target), TypeScript, Zod, Revit API 2023-2027, xUnit

---

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| `src/RevitCortex.Tools/IFC/IfcGetCapabilitiesTool.cs` | Detect IFC versions, import/export availability, revit-ifc add-in presence |
| `src/RevitCortex.Tools/IFC/IfcLinkTool.cs` | Link an IFC file into the active document via `RevitLinkType.CreateFromIFC` |
| `src/RevitCortex.Tools/IFC/IfcReloadLinkTool.cs` | Reload an existing IFC link from a new path |
| `src/RevitCortex.Tools/IFC/IfcOpenOrImportTool.cs` | Open or import an IFC file via `Application.OpenIFCDocument` |
| `src/RevitCortex.Tools/IFC/IfcExportBasicTool.cs` | Export to IFC with standard `IFCExportOptions` properties |
| `src/RevitCortex.Tools/IFC/IfcExportWithConfigurationTool.cs` | Export to IFC using named configuration (key-value extra options) |
| `src/RevitCortex.Tools/IFC/IfcListExportConfigurationsTool.cs` | List available built-in IFC export configurations |
| `src/RevitCortex.Tools/IFC/IfcGetExportConfigurationTool.cs` | Get details of a specific export configuration |
| `src/RevitCortex.Tools/IFC/IfcSetFamilyMappingFileTool.cs` | Set the family mapping file path in session for subsequent exports |
| `src/RevitCortex.Tools/IFC/IfcValidateRequestTool.cs` | Validate an IFC file path, check format, and report basic metadata |
| `server/src/schemas/ifc.ts` | All Zod schemas for IFC tools |
| `server/src/tools/ifc_get_capabilities.ts` | TS registration for ifc_get_capabilities |
| `server/src/tools/ifc_link.ts` | TS registration for ifc_link |
| `server/src/tools/ifc_reload_link.ts` | TS registration for ifc_reload_link |
| `server/src/tools/ifc_open_or_import.ts` | TS registration for ifc_open_or_import |
| `server/src/tools/ifc_export_basic.ts` | TS registration for ifc_export_basic |
| `server/src/tools/ifc_export_with_configuration.ts` | TS registration for ifc_export_with_configuration |
| `server/src/tools/ifc_list_export_configurations.ts` | TS registration for ifc_list_export_configurations |
| `server/src/tools/ifc_get_export_configuration.ts` | TS registration for ifc_get_export_configuration |
| `server/src/tools/ifc_set_family_mapping_file.ts` | TS registration for ifc_set_family_mapping_file |
| `server/src/tools/ifc_validate_request.ts` | TS registration for ifc_validate_request |

### Modified Files

| File | Change |
|------|--------|
| `server/src/tools/register.ts` | Add 10 imports + 10 entries in `toolRegistrations` array |
| `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` | Update expected tool count |

---

## Naming Convention

The plan uses underscores (`ifc_get_capabilities`) instead of dots (`ifc.get_capabilities`) for tool names. Dots are not valid in C# method names and would break the existing snake_case convention enforced by `ToolRegistrationTests.AllTools_HaveSnakeCaseNames`. The original plan's dot notation was conceptual grouping — the actual tool names use underscores.

---

## Task 1: Zod Schemas for All IFC Tools

**Files:**
- Create: `server/src/schemas/ifc.ts`

- [ ] **Step 1: Create the ifc.ts schema file**

```typescript
import { z } from "zod";

// ── ifc_get_capabilities ──
export const IfcGetCapabilitiesInput = z.object({});

// ── ifc_validate_request ──
export const IfcValidateRequestInput = z.object({
  filePath: z.string().describe("Full path to the IFC file to validate"),
});

// ── ifc_link ──
export const IfcLinkInput = z.object({
  ifcFilePath: z
    .string()
    .describe("Full path to the IFC file to link"),
  revitFilePath: z
    .string()
    .optional()
    .describe(
      "Full path for the intermediate .ifc.RVT file. If omitted, defaults to <ifcFilePath>.RVT"
    ),
  recreateLink: z
    .boolean()
    .optional()
    .default(true)
    .describe("Whether to recreate the intermediate Revit file from IFC. Default: true"),
});

// ── ifc_reload_link ──
export const IfcReloadLinkInput = z.object({
  linkTypeId: z
    .number()
    .describe("The RevitLinkType element ID of the existing IFC link"),
  newIfcFilePath: z
    .string()
    .optional()
    .describe("New IFC file path. If omitted, reloads from the original path"),
  recreateLink: z
    .boolean()
    .optional()
    .default(true)
    .describe("Whether to recreate the intermediate Revit file from IFC. Default: true"),
});

// ── ifc_open_or_import ──
export const IfcOpenOrImportInput = z.object({
  filePath: z.string().describe("Full path to the IFC file"),
  action: z
    .enum(["open", "link"])
    .optional()
    .default("open")
    .describe("'open' creates a new Revit document from IFC; 'link' creates a reference link"),
  intent: z
    .enum(["reference", "parametric"])
    .optional()
    .default("reference")
    .describe(
      "'reference' imports as lightweight reference geometry; 'parametric' imports as editable Revit elements"
    ),
  forceImport: z
    .boolean()
    .optional()
    .default(false)
    .describe("Force re-import even if a corresponding Revit file already exists"),
  autoJoin: z
    .boolean()
    .optional()
    .default(true)
    .describe("Enable auto-join at end of import"),
});

// ── ifc_export_basic ──
export const IfcExportBasicInput = z.object({
  outputDirectory: z
    .string()
    .describe("Directory for the exported IFC file. Must exist."),
  fileName: z
    .string()
    .optional()
    .default("")
    .describe("Output file name without extension. Empty = auto-name from document title"),
  fileVersion: z
    .enum([
      "Default",
      "IFC2x2",
      "IFC2x3",
      "IFC2x3CV2",
      "IFC4",
      "IFC4RV",
      "IFC4DTV",
      "IFC4x3",
    ])
    .optional()
    .default("IFC4RV")
    .describe("IFC version to export. Default: IFC4RV (IFC4 Reference View)"),
  filterViewId: z
    .number()
    .optional()
    .describe("View element ID whose visibility settings govern export. Omit to export all."),
  exportBaseQuantities: z
    .boolean()
    .optional()
    .default(false)
    .describe("Export IFC base quantities"),
  wallAndColumnSplitting: z
    .boolean()
    .optional()
    .default(false)
    .describe("Split multi-level walls and columns by level"),
  spaceBoundaryLevel: z
    .number()
    .optional()
    .default(0)
    .describe("Space boundary export level: 0, 1, or 2"),
});

// ── ifc_export_with_configuration ──
export const IfcExportWithConfigurationInput = z.object({
  outputDirectory: z
    .string()
    .describe("Directory for the exported IFC file. Must exist."),
  fileName: z
    .string()
    .optional()
    .default("")
    .describe("Output file name without extension. Empty = auto-name"),
  configurationName: z
    .string()
    .describe(
      "Name of a built-in or custom export configuration (see ifc_list_export_configurations)"
    ),
  filterViewId: z
    .number()
    .optional()
    .describe("View element ID whose visibility settings govern export"),
  overrides: z
    .record(z.string())
    .optional()
    .describe(
      "Key-value overrides applied via AddOption(). E.g. {\"ExportRoomsInView\": \"true\"}"
    ),
});

// ── ifc_list_export_configurations ──
export const IfcListExportConfigurationsInput = z.object({});

// ── ifc_get_export_configuration ──
export const IfcGetExportConfigurationInput = z.object({
  configurationName: z
    .string()
    .describe("Name of the export configuration to retrieve"),
});

// ── ifc_set_family_mapping_file ──
export const IfcSetFamilyMappingFileInput = z.object({
  filePath: z
    .string()
    .describe("Full path to the family mapping file (.txt). Set empty string to clear."),
});
```

- [ ] **Step 2: Verify the schema file compiles**

Run: `cd server && npx tsc --noEmit src/schemas/ifc.ts`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add server/src/schemas/ifc.ts
git commit -m "feat(ifc): add Zod schemas for 10 IFC file management tools"
```

---

## Task 2: ifc_get_capabilities — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcGetCapabilitiesTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reports IFC capabilities: supported versions, available actions, and
/// whether the open-source revit-ifc add-in is installed.
/// </summary>
public class IfcGetCapabilitiesTool : ICortexTool
{
    public string Name => "ifc_get_capabilities";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Get IFC capabilities: supported versions, import/export availability, revit-ifc add-in detection";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var supportedExportVersions = new List<string>();
        foreach (var v in Enum.GetValues(typeof(IFCVersion)))
        {
            if ((int)v > 0)
                supportedExportVersions.Add(v.ToString()!);
        }

        var revitIfcAddinInstalled = DetectRevitIfcAddin();

        var capabilities = new
        {
            supportedExportVersions,
            supportedImportActions = new[] { "open", "link" },
            supportedImportIntents = new[] { "reference", "parametric" },
            revitIfcAddinInstalled,
            canExport = true,
            canImport = true,
            canLink = true,
        };

        return CortexResult<object>.Ok(capabilities);
    }

    private static bool DetectRevitIfcAddin()
    {
        // The open-source revit-ifc add-in installs IFCExporterUI*.dll
        // in the Revit add-ins folder. Check if it's loaded.
        try
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Any(a => a.GetName().Name != null &&
                          a.GetName().Name!.StartsWith("IFCExporter", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcGetCapabilitiesTool.cs
git commit -m "feat(ifc): add ifc_get_capabilities tool"
```

---

## Task 3: ifc_validate_request — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcValidateRequestTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Validates an IFC file path: checks existence, extension, file size,
/// and reads the IFC header line to detect schema version.
/// </summary>
public class IfcValidateRequestTool : ICortexTool
{
    public string Name => "ifc_validate_request";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Validate an IFC file path, check format, and report basic metadata";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to an IFC file");

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"File not found: {filePath}",
                suggestion: "Check the file path and ensure it exists");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".ifc" && ext != ".ifczip" && ext != ".ifcxml")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unsupported extension: {ext}",
                suggestion: "Supported extensions: .ifc, .ifczip, .ifcxml");

        var fileInfo = new FileInfo(filePath);
        var fileSizeMb = Math.Round(fileInfo.Length / (1024.0 * 1024.0), 2);

        // Try to detect IFC schema version from the header
        string? detectedSchema = null;
        try
        {
            using var reader = new StreamReader(filePath);
            for (int i = 0; i < 50; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (line.Contains("FILE_SCHEMA"))
                {
                    // FILE_SCHEMA(('IFC4'));  or  FILE_SCHEMA(('IFC2X3'));
                    var start = line.IndexOf("'", StringComparison.Ordinal);
                    var end = start >= 0 ? line.IndexOf("'", start + 1, StringComparison.Ordinal) : -1;
                    if (start >= 0 && end > start)
                        detectedSchema = line.Substring(start + 1, end - start - 1);
                    break;
                }
            }
        }
        catch
        {
            // Non-critical — just can't read header
        }

        return CortexResult<object>.Ok(new
        {
            valid = true,
            filePath,
            extension = ext,
            fileSizeMb,
            detectedSchema,
            lastModified = fileInfo.LastWriteTimeUtc.ToString("o"),
        });
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcValidateRequestTool.cs
git commit -m "feat(ifc): add ifc_validate_request tool"
```

---

## Task 4: ifc_link — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcLinkTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Links an IFC file into the active document using RevitLinkType.CreateFromIFC.
/// Creates an intermediate .ifc.RVT file and a RevitLinkInstance.
/// </summary>
public class IfcLinkTool : ICortexTool
{
    public string Name => "ifc_link";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Link an IFC file into the active Revit document";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var ifcFilePath = input["ifcFilePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(ifcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "ifcFilePath is required",
                suggestion: "Provide the full path to the IFC file to link");

        if (!File.Exists(ifcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"IFC file not found: {ifcFilePath}");

        var revitFilePath = input["revitFilePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(revitFilePath))
            revitFilePath = ifcFilePath + ".RVT";

        var recreateLink = input["recreateLink"]?.Value<bool>() ?? true;

        if (!session.RequestConfirmation("link IFC file", 1, $"Link: {Path.GetFileName(ifcFilePath)}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var options = new RevitLinkOptions(false);
            var linkResult = RevitLinkType.CreateFromIFC(
                doc!, ifcFilePath, revitFilePath, recreateLink, options);

            var linkTypeId = linkResult.ElementId;
            if (linkTypeId == null || linkTypeId == ElementId.InvalidElementId)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "CreateFromIFC returned an invalid element ID");

            // Place an instance of the link
            var instance = RevitLinkInstance.Create(doc!, linkTypeId);

            return CortexResult<object>.Ok(new
            {
                linkTypeId = ToolHelpers.GetElementIdValue(linkTypeId),
                instanceId = ToolHelpers.GetElementIdValue(instance.Id),
                name = instance.Name,
                ifcFilePath,
                revitFilePath,
                recreatedLink = recreateLink,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to link IFC file: {ex.Message}",
                suggestion: "Ensure the IFC file is valid and Revit can access the path");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcLinkTool.cs
git commit -m "feat(ifc): add ifc_link tool"
```

---

## Task 5: ifc_reload_link — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcReloadLinkTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reloads an existing IFC link, optionally from a new IFC file path.
/// </summary>
public class IfcReloadLinkTool : ICortexTool
{
    public string Name => "ifc_reload_link";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reload an existing IFC link, optionally from a new IFC file path";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var linkTypeId = input["linkTypeId"]?.Value<long>() ?? 0;
        if (linkTypeId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "linkTypeId is required",
                suggestion: "Provide the RevitLinkType element ID of the IFC link");

        var elementId = ToolHelpers.ToElementId(linkTypeId);
        var linkType = doc!.GetElement(elementId) as RevitLinkType;
        if (linkType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"RevitLinkType {linkTypeId} not found");

        // Get current paths
        var currentIfcPath = "";
        var currentRvtPath = "";
        try
        {
            var extRef = linkType.GetExternalFileReference();
            if (extRef != null)
                currentRvtPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetAbsolutePath());
        }
        catch { /* path unavailable */ }

        var newIfcFilePath = input["newIfcFilePath"]?.Value<string>();
        var recreateLink = input["recreateLink"]?.Value<bool>() ?? true;

        // Determine the IFC path to use
        var ifcPath = string.IsNullOrWhiteSpace(newIfcFilePath) ? currentIfcPath : newIfcFilePath;

        if (!string.IsNullOrWhiteSpace(newIfcFilePath) && !File.Exists(newIfcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"New IFC file not found: {newIfcFilePath}");

        var description = string.IsNullOrWhiteSpace(newIfcFilePath)
            ? $"Reload IFC link '{linkType.Name}'"
            : $"Reload IFC link '{linkType.Name}' from '{Path.GetFileName(newIfcFilePath)}'";

        if (!session.RequestConfirmation("reload IFC link", 1, description))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            // Reload the link — this uses the standard RevitLinkType reload
            // For IFC links, Revit will re-process the IFC file
            if (!string.IsNullOrWhiteSpace(newIfcFilePath))
            {
                var revitFilePath = newIfcFilePath + ".RVT";
                var options = new RevitLinkOptions(false);
                RevitLinkType.CreateFromIFC(doc!, newIfcFilePath, revitFilePath, recreateLink, options);
            }
            else
            {
                var result = linkType.Reload();
                if (result.LoadResult != LinkedFileStatus.Loaded)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Reload failed with status: {result.LoadResult}");
            }

            return CortexResult<object>.Ok(new
            {
                linkTypeId,
                name = linkType.Name,
                action = string.IsNullOrWhiteSpace(newIfcFilePath) ? "reloaded" : "reloaded_from_new_path",
                newIfcFilePath,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to reload IFC link: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcReloadLinkTool.cs
git commit -m "feat(ifc): add ifc_reload_link tool"
```

---

## Task 6: ifc_open_or_import — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcOpenOrImportTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.IO;
using Autodesk.Revit.DB.IFC;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Opens or imports an IFC file using Application.OpenIFCDocument with IFCImportOptions.
/// Action "open" creates a new Revit document; action "link" creates a reference.
/// </summary>
public class IfcOpenOrImportTool : ICortexTool
{
    public string Name => "ifc_open_or_import";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Open or import an IFC file into Revit";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to the IFC file");

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"IFC file not found: {filePath}");

        var actionStr = input["action"]?.Value<string>() ?? "open";
        var intentStr = input["intent"]?.Value<string>() ?? "reference";
        var forceImport = input["forceImport"]?.Value<bool>() ?? false;
        var autoJoin = input["autoJoin"]?.Value<bool>() ?? true;

        if (!session.RequestConfirmation($"{actionStr} IFC file", 1, $"File: {Path.GetFileName(filePath)}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var options = new IFCImportOptions();

            // Set action
            options.Action = actionStr.ToLowerInvariant() switch
            {
                "link" => IFCImportAction.Link,
                _ => IFCImportAction.Open,
            };

            // Set intent
            options.Intent = intentStr.ToLowerInvariant() switch
            {
                "parametric" => IFCImportIntent.Parametric,
                _ => IFCImportIntent.Reference,
            };

            options.ForceImport = forceImport;
            options.AutoJoin = autoJoin;

            // Get the Application object from session
            var app = session.Store.Get<object>("application") as Autodesk.Revit.ApplicationServices.Application;
            if (app == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Revit Application not available in session");

            var newDoc = app.OpenIFCDocument(filePath, options);

            return CortexResult<object>.Ok(new
            {
                action = actionStr,
                intent = intentStr,
                filePath,
                documentTitle = newDoc?.Title ?? "unknown",
                success = newDoc != null,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to {actionStr} IFC file: {ex.Message}",
                suggestion: "Ensure the IFC file is valid and Revit supports this operation");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcOpenOrImportTool.cs
git commit -m "feat(ifc): add ifc_open_or_import tool"
```

---

## Task 7: ifc_export_basic — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcExportBasicTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Exports the active document to IFC using standard IFCExportOptions.
/// </summary>
public class IfcExportBasicTool : ICortexTool
{
    public string Name => "ifc_export_basic";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Export the active Revit document to IFC with standard options";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var outputDirectory = input["outputDirectory"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "outputDirectory is required");

        if (!Directory.Exists(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Output directory does not exist: {outputDirectory}",
                suggestion: "Create the directory first or provide an existing path");

        var fileName = input["fileName"]?.Value<string>() ?? "";
        var fileVersionStr = input["fileVersion"]?.Value<string>() ?? "IFC4RV";
        var filterViewIdRaw = input["filterViewId"]?.Value<long>();
        var exportBaseQuantities = input["exportBaseQuantities"]?.Value<bool>() ?? false;
        var wallAndColumnSplitting = input["wallAndColumnSplitting"]?.Value<bool>() ?? false;
        var spaceBoundaryLevel = input["spaceBoundaryLevel"]?.Value<int>() ?? 0;

        if (!Enum.TryParse<IFCVersion>(fileVersionStr, ignoreCase: true, out var fileVersion))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown IFC version: {fileVersionStr}",
                suggestion: "Use: Default, IFC2x2, IFC2x3, IFC2x3CV2, IFC4, IFC4RV, IFC4DTV, IFC4x3");

        if (!session.RequestConfirmation("export IFC", 1, $"Export to {outputDirectory}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var options = new IFCExportOptions
            {
                FileVersion = fileVersion,
                ExportBaseQuantities = exportBaseQuantities,
                WallAndColumnSplitting = wallAndColumnSplitting,
                SpaceBoundaryLevel = spaceBoundaryLevel,
            };

            if (filterViewIdRaw.HasValue)
                options.FilterViewId = ToolHelpers.ToElementId(filterViewIdRaw.Value);

            // Check session for a family mapping file set by ifc_set_family_mapping_file
            var mappingFile = session.Store.Get<string>("ifc_family_mapping_file");
            if (!string.IsNullOrWhiteSpace(mappingFile) && File.Exists(mappingFile))
                options.FamilyMappingFile = mappingFile;

            using var tx = new Transaction(doc!, "RevitCortex: Export IFC");
            tx.Start();
            var success = doc!.Export(outputDirectory, fileName, options);
            tx.Commit();

            var actualFileName = string.IsNullOrEmpty(fileName) ? doc.Title : fileName;

            return CortexResult<object>.Ok(new
            {
                success,
                outputDirectory,
                fileName = actualFileName + ".ifc",
                fileVersion = fileVersionStr,
                exportBaseQuantities,
                wallAndColumnSplitting,
                spaceBoundaryLevel,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"IFC export failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcExportBasicTool.cs
git commit -m "feat(ifc): add ifc_export_basic tool"
```

---

## Task 8: ifc_export_with_configuration — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcExportWithConfigurationTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Exports to IFC using a named configuration. Configurations are predefined
/// sets of options that map to common IFC MVDs. Extra key-value overrides
/// are passed via IFCExportOptions.AddOption().
/// </summary>
public class IfcExportWithConfigurationTool : ICortexTool
{
    public string Name => "ifc_export_with_configuration";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Export to IFC using a named configuration with optional overrides";

    // Built-in configurations with their default option sets
    private static readonly Dictionary<string, Dictionary<string, string>> BuiltInConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IFC4 Reference View"] = new()
        {
            { "IFCVersion", "IFC4RV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
        ["IFC4 Design Transfer View"] = new()
        {
            { "IFCVersion", "IFC4DTV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "1" },
            { "WallAndColumnSplitting", "true" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
        ["IFC2x3 Coordination View 2.0"] = new()
        {
            { "IFCVersion", "IFC2x3CV2" },
            { "ExportBaseQuantities", "false" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
        ["IFC2x3 COBie 2.4"] = new()
        {
            { "IFCVersion", "IFCCOBIE" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "2" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "true" },
            { "ExportSchedulesAsPsets", "true" },
        },
        ["IFC4x3"] = new()
        {
            { "IFCVersion", "IFC4x3" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var outputDirectory = input["outputDirectory"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "outputDirectory is required");

        if (!Directory.Exists(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Output directory does not exist: {outputDirectory}");

        var fileName = input["fileName"]?.Value<string>() ?? "";
        var configName = input["configurationName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(configName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "configurationName is required",
                suggestion: $"Available: {string.Join(", ", BuiltInConfigs.Keys)}");

        if (!BuiltInConfigs.TryGetValue(configName!, out var configOptions))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Configuration '{configName}' not found",
                suggestion: $"Available: {string.Join(", ", BuiltInConfigs.Keys)}");

        var filterViewIdRaw = input["filterViewId"]?.Value<long>();
        var overrides = input["overrides"]?.ToObject<Dictionary<string, string>>();

        if (!session.RequestConfirmation("export IFC", 1,
            $"Export with config '{configName}' to {outputDirectory}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            // Parse the IFC version from the config
            var versionStr = configOptions.GetValueOrDefault("IFCVersion", "IFC4RV");
            Enum.TryParse<IFCVersion>(versionStr, ignoreCase: true, out var fileVersion);

            var options = new IFCExportOptions { FileVersion = fileVersion };

            if (filterViewIdRaw.HasValue)
                options.FilterViewId = ToolHelpers.ToElementId(filterViewIdRaw.Value);

            // Apply base quantities and splitting from config
            if (configOptions.TryGetValue("ExportBaseQuantities", out var ebq))
                options.ExportBaseQuantities = ebq.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (configOptions.TryGetValue("WallAndColumnSplitting", out var wcs))
                options.WallAndColumnSplitting = wcs.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (configOptions.TryGetValue("SpaceBoundaries", out var sb) && int.TryParse(sb, out var sbLevel))
                options.SpaceBoundaryLevel = sbLevel;

            // Apply all config options via AddOption (for revit-ifc compatibility)
            foreach (var kvp in configOptions)
            {
                if (kvp.Key == "IFCVersion") continue; // Already set via FileVersion
                options.AddOption(kvp.Key, kvp.Value);
            }

            // Apply overrides (these take precedence)
            if (overrides != null)
            {
                foreach (var kvp in overrides)
                    options.AddOption(kvp.Key, kvp.Value);
            }

            // Apply family mapping from session
            var mappingFile = session.Store.Get<string>("ifc_family_mapping_file");
            if (!string.IsNullOrWhiteSpace(mappingFile) && File.Exists(mappingFile))
                options.FamilyMappingFile = mappingFile;

            using var tx = new Transaction(doc!, "RevitCortex: Export IFC (configured)");
            tx.Start();
            var success = doc!.Export(outputDirectory, fileName, options);
            tx.Commit();

            var actualFileName = string.IsNullOrEmpty(fileName) ? doc.Title : fileName;

            return CortexResult<object>.Ok(new
            {
                success,
                configurationName = configName,
                outputDirectory,
                fileName = actualFileName + ".ifc",
                fileVersion = fileVersion.ToString(),
                overridesApplied = overrides?.Count ?? 0,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"IFC export failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcExportWithConfigurationTool.cs
git commit -m "feat(ifc): add ifc_export_with_configuration tool"
```

---

## Task 9: ifc_list_export_configurations + ifc_get_export_configuration — C# Tools

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcListExportConfigurationsTool.cs`
- Create: `src/RevitCortex.Tools/IFC/IfcGetExportConfigurationTool.cs`

- [ ] **Step 1: Create ifc_list_export_configurations tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Lists available IFC export configurations (built-in presets).
/// </summary>
public class IfcListExportConfigurationsTool : ICortexTool
{
    public string Name => "ifc_list_export_configurations";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "List available IFC export configurations";

    internal static readonly Dictionary<string, ConfigInfo> Configurations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IFC4 Reference View"] = new("IFC4RV", "IFC4 Reference View — lightweight geometry for coordination and reference"),
        ["IFC4 Design Transfer View"] = new("IFC4DTV", "IFC4 Design Transfer View — full parametric data for design handoff"),
        ["IFC2x3 Coordination View 2.0"] = new("IFC2x3CV2", "IFC 2x3 CV2 — widely supported legacy format"),
        ["IFC2x3 COBie 2.4"] = new("IFCCOBIE", "IFC 2x3 COBie — facility management handover"),
        ["IFC4x3"] = new("IFC4x3", "IFC 4x3 — latest standard with infrastructure support"),
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var configs = Configurations.Select(kvp => new
        {
            name = kvp.Key,
            ifcVersion = kvp.Value.Version,
            description = kvp.Value.Description,
        }).ToList();

        return CortexResult<object>.Ok(new
        {
            count = configs.Count,
            configurations = configs,
        });
    }

    internal record ConfigInfo(string Version, string Description);
}
```

- [ ] **Step 2: Create ifc_get_export_configuration tool**

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Returns the full details of a specific IFC export configuration.
/// </summary>
public class IfcGetExportConfigurationTool : ICortexTool
{
    public string Name => "ifc_get_export_configuration";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Get details of a specific IFC export configuration";

    // Full option sets for each configuration
    private static readonly Dictionary<string, Dictionary<string, string>> ConfigDetails = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IFC4 Reference View"] = new()
        {
            { "IFCVersion", "IFC4RV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC4 Design Transfer View"] = new()
        {
            { "IFCVersion", "IFC4DTV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "1" },
            { "WallAndColumnSplitting", "true" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC2x3 Coordination View 2.0"] = new()
        {
            { "IFCVersion", "IFC2x3CV2" },
            { "ExportBaseQuantities", "false" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC2x3 COBie 2.4"] = new()
        {
            { "IFCVersion", "IFCCOBIE" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "2" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "true" },
            { "ExportSchedulesAsPsets", "true" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "true" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC4x3"] = new()
        {
            { "IFCVersion", "IFC4x3" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var configName = input["configurationName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(configName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "configurationName is required",
                suggestion: "Use ifc_list_export_configurations to see available names");

        if (!ConfigDetails.TryGetValue(configName!, out var options))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Configuration '{configName}' not found",
                suggestion: $"Available: {string.Join(", ", ConfigDetails.Keys)}");

        if (!IfcListExportConfigurationsTool.Configurations.TryGetValue(configName!, out var info))
            info = new IfcListExportConfigurationsTool.ConfigInfo("unknown", "");

        return CortexResult<object>.Ok(new
        {
            name = configName,
            ifcVersion = info.Version,
            description = info.Description,
            options,
        });
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcListExportConfigurationsTool.cs src/RevitCortex.Tools/IFC/IfcGetExportConfigurationTool.cs
git commit -m "feat(ifc): add ifc_list_export_configurations and ifc_get_export_configuration tools"
```

---

## Task 10: ifc_set_family_mapping_file — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcSetFamilyMappingFileTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Sets the IFC family mapping file path in the session store.
/// Subsequent ifc_export_basic and ifc_export_with_configuration calls
/// will use this mapping file automatically.
/// </summary>
public class IfcSetFamilyMappingFileTool : ICortexTool
{
    public string Name => "ifc_set_family_mapping_file";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Set the family mapping file for IFC exports (persists in session)";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (filePath == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to a .txt family mapping file, or empty string to clear");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Clear the mapping
            session.Store.Set<string?>("ifc_family_mapping_file", null);
            return CortexResult<object>.Ok(new
            {
                action = "cleared",
                message = "Family mapping file cleared from session",
            });
        }

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"File not found: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".txt")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Expected .txt file, got: {ext}",
                suggestion: "The family mapping file must be a .txt file");

        session.Store.Set("ifc_family_mapping_file", filePath);

        return CortexResult<object>.Ok(new
        {
            action = "set",
            filePath,
            message = "Family mapping file set. Subsequent IFC exports will use this mapping.",
        });
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcSetFamilyMappingFileTool.cs
git commit -m "feat(ifc): add ifc_set_family_mapping_file tool"
```

---

## Task 11: TypeScript Tool Registrations (all 10 tools)

**Files:**
- Create: `server/src/tools/ifc_get_capabilities.ts`
- Create: `server/src/tools/ifc_validate_request.ts`
- Create: `server/src/tools/ifc_link.ts`
- Create: `server/src/tools/ifc_reload_link.ts`
- Create: `server/src/tools/ifc_open_or_import.ts`
- Create: `server/src/tools/ifc_export_basic.ts`
- Create: `server/src/tools/ifc_export_with_configuration.ts`
- Create: `server/src/tools/ifc_list_export_configurations.ts`
- Create: `server/src/tools/ifc_get_export_configuration.ts`
- Create: `server/src/tools/ifc_set_family_mapping_file.ts`

- [ ] **Step 1: Create ifc_get_capabilities.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcGetCapabilitiesInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcGetCapabilitiesTool(server: McpServer): void {
  server.tool(
    "ifc_get_capabilities",
    "Get IFC capabilities: supported versions, import/export availability, revit-ifc add-in detection.",
    IfcGetCapabilitiesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_get_capabilities", args);
        });
        return toolResponse("ifc_get_capabilities", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_get_capabilities", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 2: Create ifc_validate_request.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcValidateRequestInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcValidateRequestTool(server: McpServer): void {
  server.tool(
    "ifc_validate_request",
    "Validate an IFC file path: check existence, format, size, and detect schema version from header.",
    IfcValidateRequestInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_validate_request", args);
        });
        return toolResponse("ifc_validate_request", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_validate_request", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 3: Create ifc_link.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcLinkInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcLinkTool(server: McpServer): void {
  server.tool(
    "ifc_link",
    "Link an IFC file into the active Revit document. Creates an intermediate .ifc.RVT and a RevitLinkInstance.",
    IfcLinkInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_link", args);
        });
        return toolResponse("ifc_link", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_link", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 4: Create ifc_reload_link.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcReloadLinkInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcReloadLinkTool(server: McpServer): void {
  server.tool(
    "ifc_reload_link",
    "Reload an existing IFC link, optionally from a new IFC file path.",
    IfcReloadLinkInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_reload_link", args);
        });
        return toolResponse("ifc_reload_link", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_reload_link", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 5: Create ifc_open_or_import.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcOpenOrImportInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcOpenOrImportTool(server: McpServer): void {
  server.tool(
    "ifc_open_or_import",
    "Open or import an IFC file into Revit. 'open' creates a new document; 'link' creates a reference.",
    IfcOpenOrImportInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_open_or_import", args);
        });
        return toolResponse("ifc_open_or_import", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_open_or_import", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 6: Create ifc_export_basic.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcExportBasicInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcExportBasicTool(server: McpServer): void {
  server.tool(
    "ifc_export_basic",
    "Export the active Revit document to IFC with standard options (version, view filter, base quantities).",
    IfcExportBasicInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_export_basic", args);
        });
        return toolResponse("ifc_export_basic", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_export_basic", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 7: Create ifc_export_with_configuration.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcExportWithConfigurationInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcExportWithConfigurationTool(server: McpServer): void {
  server.tool(
    "ifc_export_with_configuration",
    "Export to IFC using a named configuration (e.g. 'IFC4 Reference View') with optional key-value overrides.",
    IfcExportWithConfigurationInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_export_with_configuration", args);
        });
        return toolResponse("ifc_export_with_configuration", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_export_with_configuration", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 8: Create ifc_list_export_configurations.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcListExportConfigurationsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcListExportConfigurationsTool(server: McpServer): void {
  server.tool(
    "ifc_list_export_configurations",
    "List available IFC export configurations (built-in presets like IFC4 Reference View, IFC2x3 CV2, etc.).",
    IfcListExportConfigurationsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_list_export_configurations", args);
        });
        return toolResponse("ifc_list_export_configurations", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_list_export_configurations", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 9: Create ifc_get_export_configuration.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcGetExportConfigurationInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcGetExportConfigurationTool(server: McpServer): void {
  server.tool(
    "ifc_get_export_configuration",
    "Get the full details and option set of a specific IFC export configuration.",
    IfcGetExportConfigurationInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_get_export_configuration", args);
        });
        return toolResponse("ifc_get_export_configuration", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_get_export_configuration", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 10: Create ifc_set_family_mapping_file.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcSetFamilyMappingFileInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcSetFamilyMappingFileTool(server: McpServer): void {
  server.tool(
    "ifc_set_family_mapping_file",
    "Set the family mapping file for IFC exports. Persists in session for subsequent export calls.",
    IfcSetFamilyMappingFileInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_set_family_mapping_file", args);
        });
        return toolResponse("ifc_set_family_mapping_file", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_set_family_mapping_file", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 11: Commit all TS tool files**

```bash
git add server/src/tools/ifc_*.ts
git commit -m "feat(ifc): add TypeScript tool registrations for 10 IFC tools"
```

---

## Task 12: Register All IFC Tools in register.ts

**Files:**
- Modify: `server/src/tools/register.ts`

- [ ] **Step 1: Add imports at the top of register.ts**

Add these imports after the existing import block:

```typescript
import { registerIfcGetCapabilitiesTool } from "./ifc_get_capabilities.js";
import { registerIfcValidateRequestTool } from "./ifc_validate_request.js";
import { registerIfcLinkTool } from "./ifc_link.js";
import { registerIfcReloadLinkTool } from "./ifc_reload_link.js";
import { registerIfcOpenOrImportTool } from "./ifc_open_or_import.js";
import { registerIfcExportBasicTool } from "./ifc_export_basic.js";
import { registerIfcExportWithConfigurationTool } from "./ifc_export_with_configuration.js";
import { registerIfcListExportConfigurationsTool } from "./ifc_list_export_configurations.js";
import { registerIfcGetExportConfigurationTool } from "./ifc_get_export_configuration.js";
import { registerIfcSetFamilyMappingFileTool } from "./ifc_set_family_mapping_file.js";
```

- [ ] **Step 2: Add entries to toolRegistrations array**

Add these entries to the `toolRegistrations` array (before the closing `]`):

```typescript
  { name: "ifc_get_capabilities", register: registerIfcGetCapabilitiesTool },
  { name: "ifc_validate_request", register: registerIfcValidateRequestTool },
  { name: "ifc_link", register: registerIfcLinkTool },
  { name: "ifc_reload_link", register: registerIfcReloadLinkTool },
  { name: "ifc_open_or_import", register: registerIfcOpenOrImportTool },
  { name: "ifc_export_basic", register: registerIfcExportBasicTool },
  { name: "ifc_export_with_configuration", register: registerIfcExportWithConfigurationTool },
  { name: "ifc_list_export_configurations", register: registerIfcListExportConfigurationsTool },
  { name: "ifc_get_export_configuration", register: registerIfcGetExportConfigurationTool },
  { name: "ifc_set_family_mapping_file", register: registerIfcSetFamilyMappingFileTool },
```

- [ ] **Step 3: Build TS server to verify**

Run: `cd server && npm run build`
Expected: Build succeeded, no errors

- [ ] **Step 4: Commit**

```bash
git add server/src/tools/register.ts
git commit -m "feat(ifc): register 10 IFC tools in TS server"
```

---

## Task 13: Update Tool Count Test

**Files:**
- Modify: `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs:104`

- [ ] **Step 1: Update the expected tool count**

Change line 104 from:

```csharp
Assert.True(AllToolTypes.Count >= 113,
```

To:

```csharp
Assert.True(AllToolTypes.Count >= 123,
```

And update the message:

```csharp
$"Expected at least 123 tools but found {AllToolTypes.Count}. " +
```

- [ ] **Step 2: Run tests**

Run: `dotnet test -c "Debug R25"`
Expected: All tests pass (ToolRegistrationTests includes the 10 new IFC tools)

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "test: update expected tool count for 10 new IFC tools"
```

---

## Task 14: Update Read-Only Tool Prefixes

**Files:**
- Verify: `src/RevitCortex.Plugin/CortexRouter.cs`

- [ ] **Step 1: Verify read-only classification**

The existing `ReadOnlyPrefixes` in CortexRouter already cover the IFC read-only tools:

| Tool | Prefix Match | Classification |
|------|-------------|----------------|
| `ifc_get_capabilities` | `get_` | Read-only ✓ |
| `ifc_validate_request` | None | **Write** — needs fix |
| `ifc_link` | None | Write ✓ |
| `ifc_reload_link` | None | Write ✓ |
| `ifc_open_or_import` | None | Write ✓ |
| `ifc_export_basic` | `export_` | Read-only ✓ |
| `ifc_export_with_configuration` | None | **Write** — but export is read-only conceptually |
| `ifc_list_export_configurations` | `list_` | Read-only ✓ |
| `ifc_get_export_configuration` | `get_` | Read-only ✓ |
| `ifc_set_family_mapping_file` | None | Write ✓ |

Two tools need attention:
- `ifc_validate_request` is read-only but has no matching prefix → add to hardcoded names
- `ifc_export_with_configuration` is read-only (export) but doesn't start with `export_` → add to hardcoded names

- [ ] **Step 2: Add hardcoded read-only tool names**

In `CortexRouter.cs`, find the `ReadOnlyPrefixes` array and update the `IsReadOnlyTool` method. Add `ifc_validate_request` and `ifc_export_with_configuration` to the hardcoded names list alongside `say_hello`, `clash_detection`, and `lines_per_view_count`:

Find the existing pattern in `IsReadOnlyTool` that checks for exact names and add:
- `"ifc_validate_request"`
- `"ifc_export_with_configuration"`

- [ ] **Step 3: Build to verify**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 4: Run tests**

Run: `dotnet test -c "Debug R25"`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Plugin/CortexRouter.cs
git commit -m "fix: add IFC read-only tools to CortexRouter whitelist"
```

---

## Task 15: Full Build & Test Verification

- [ ] **Step 1: Build C# for R25**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 2: Build TypeScript server**

Run: `cd server && npm run build`
Expected: Build succeeded

- [ ] **Step 3: Run all tests**

Run: `dotnet test -c "Debug R25"`
Expected: All tests pass, including:
- `ToolRegistrationTests.AllTools_HaveUniqueNames` — 10 new IFC tools have unique names
- `ToolRegistrationTests.AllTools_HaveSnakeCaseNames` — all names match `^[a-z][a-z0-9_]*$`
- `ToolRegistrationTests.TypeScript_ToolRegistrations_MatchCSharp` — all 10 IFC tools registered in TS
- `ToolRegistrationTests.ToolCount_MatchesExpected` — count >= 123

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git add -A
git commit -m "feat(ifc): complete IFC file management — 10 tools implemented"
```
