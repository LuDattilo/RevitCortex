# RevitCortex -- AI Assistant Guide

## Project Overview

RevitCortex is a next-generation MCP (Model Context Protocol) server for Autodesk Revit. It improves on the original mcp-servers-for-revit with typed errors, session state, and dynamic tool discovery. Tools are rewritten from scratch -- not copied from the fork reference.

## Supported Revit Versions

| Revit Version | Target Framework |
|---------------|-----------------|
| 2023          | net48           |
| 2024          | net48           |
| 2025          | net8.0-windows  |
| 2026          | net8.0-windows  |

## Project Structure

```
RevitCortex/
  RevitCortex.sln
  nuget.config
  src/
    RevitCortex.Core/         Core types (no Revit dependency)
      Discovery/                DocumentCapabilities, IDocumentAnalyzer
      Results/                  CortexResult<T>, CortexError, CortexErrorCode
      Session/                  CortexSession, ISessionStore, SessionStore
      Tools/                    ICortexTool interface
    RevitCortex.Plugin/       Revit add-in (ExternalApplication)
      Communication/            SocketService, JsonRpcModels
      Discovery/                DocumentAnalyzer, LocaleDetector
      CortexRouter.cs           Request dispatch
      RevitCortexApp.cs         Entry point
    RevitCortex.Tools/        Tool implementations
      Meta/                     SayHelloTool
    RevitCortex.Tests/        Unit tests (xUnit)
      Discovery/                DocumentCapabilitiesTests
      Results/                  CortexResultTests
      Router/                   CortexRouterTests, FakeTool, FakeAnalyzer
      Session/                  SessionStoreTests
  server/                     TypeScript MCP server (stdio transport)
    src/
      connection/               TCP connection to Plugin
      logging/                  Structured logging
      schemas/                  Zod schemas
      tools/                    Tool definitions
    index.ts                    Entry point
```

## Architecture: Layer Cake

```
MCP Server (TypeScript) -> SocketService -> CortexRouter -> ICortexTool
                                                |
                                          CortexSession
                                                |
                                    DocumentCapabilities
```

- **MCP Server (TypeScript)** -- Zod validation, JSON schema generation, stdio transport to Claude.
- **SocketService (C#)** -- TCP listener, JSON-RPC framing between TS server and C# plugin.
- **CortexRouter (C#)** -- Deserializes the JSON-RPC request, finds the matching ICortexTool by name, invokes it with CortexSession, returns CortexResult.
- **ICortexTool (C#)** -- Unified interface for all tools.
- **CortexSession (C#)** -- Shared state facade passed to every tool: session store, document capabilities, detected locale.
- **CortexResult\<T\> (C#)** -- Typed response envelope with success/error discriminator and structured error codes.
- **DocumentAnalyzer (C#)** -- Scans the active Revit document to populate DocumentCapabilities, enabling/disabling dynamic tools.

## Build Commands

### C# (from repo root)

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2025
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2024
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2023
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2026
```

### TypeScript MCP Server

```bash
cd server && npm install && npm run build
```

### Tests

```bash
dotnet test -c "Debug R25"
```

## Key Patterns

### CortexResult\<T\>

Every tool returns a typed result. Never throw exceptions or return raw strings.

```csharp
// Success
return CortexResult<object>.Ok(new { greeting = "Hello" });

// Failure with structured error
return CortexResult<object>.Fail(
    CortexErrorCode.ElementNotFound,
    "Element 12345 does not exist in the active document",
    suggestion: "Check the element ID or ensure the correct document is open");
```

Error codes: `ElementNotFound` (100), `PermissionDenied` (200), `TransactionFailed` (300), `InvalidInput` (400), `Timeout` (500), `Unknown` (900).

### ICortexTool

Every tool implements this interface:

```csharp
public interface ICortexTool
{
    string Name { get; }              // MCP tool name, e.g. "get_element_parameters"
    string Category { get; }          // Domain group, e.g. "Elements"
    bool RequiresDocument { get; }    // Needs an open Revit document?
    bool IsDynamic { get; }           // Only visible when capabilities match?
    CortexResult<object> Execute(JObject input, CortexSession session);
}
```

### CortexSession

Facade passed to every tool. Provides shared state without direct Revit globals.

```csharp
public class CortexSession
{
    public ISessionStore Store { get; }
    public DocumentCapabilities Capabilities { get; }
    public string DetectedLocale { get; }

    public void Reinitialize(DocumentCapabilities capabilities, string locale);
}
```

- `Store` -- key/value cache that persists across tool calls within a session.
- `Capabilities` -- discovered at document open by DocumentAnalyzer.
- `DetectedLocale` -- detected by LocaleDetector ("en", "it", "fr", "de").

### IsDynamic Convention

Tools with `IsDynamic = true` are only registered/visible when the active document has matching `DocumentCapabilities`. This keeps the tool list relevant and avoids errors from calling tools on incompatible documents.

When DocumentAnalyzer scans a document:
1. It populates `DocumentCapabilities` (present categories, worksets, phases, etc.).
2. It calls `capabilities.EnableTool("tool_name")` for each dynamic tool whose prerequisites are met.
3. CortexRouter only exposes tools where `!IsDynamic || capabilities.IsToolEnabled(tool.Name)`.

## Fork Reference

The original MCP server lives at:

```
C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit
```

This is **read-only reference material**. Tools are rewritten from scratch using the new architecture -- never copy code directly.

### Migration Guidance

When porting a tool from the original server:
1. Create a new class in `RevitCortex.Tools/` implementing `ICortexTool`.
2. Replace raw string returns with `CortexResult<object>.Ok(...)` / `.Fail(...)`.
3. Replace direct `Document` access with `CortexSession` (the Plugin layer injects the document).
4. Set `IsDynamic = true` if the tool only applies to certain document types.
5. Add Zod schema in `server/src/schemas/` and register in `server/src/tools/`.

## Revit Locale Detection

Revit localizes category and parameter names based on installation language. Detect the active locale from tool responses (parameter names reveal the language) rather than assuming one language.

**Always prefer OST_\* codes** (e.g., `OST_StructuralFraming`, `OST_Walls`) for category references -- these are language-independent and work in any locale.

### Common OST Codes

| Code | Elements |
|------|----------|
| `OST_StructuralFraming` | Beams, joists, braces |
| `OST_StructuralColumns` | Columns |
| `OST_StructuralFoundation` | Foundations |
| `OST_Walls` | Walls |
| `OST_Floors` | Floors |
| `OST_Doors` | Doors |
| `OST_Windows` | Windows |
| `OST_Rooms` | Rooms |
| `OST_GenericModel` | Generic models |
