# RevitCortex

A next-generation MCP (Model Context Protocol) server for Autodesk Revit featuring typed errors, session state, and dynamic tool discovery.

RevitCortex replaces raw string responses with structured `CortexResult<T>` objects, keeps per-document state in `CortexSession`, and hides tools that do not apply to the active document via `DocumentCapabilities` analysis.

## Supported Revit Versions

| Version | Target Framework |
|---------|-----------------|
| 2023    | net48           |
| 2024    | net48           |
| 2025    | net8.0-windows  |
| 2026    | net8.0-windows  |

## Architecture

RevitCortex follows a **Layer Cake** architecture where each layer has a single responsibility:

```
 Claude / LLM
      |
 MCP Server (TypeScript)    Zod validation, JSON schema, stdio transport
      |
 SocketService (C#)         TCP bridge, JSON-RPC framing
      |
 CortexRouter (C#)          Deserialize request, find tool by name, manage session
      |
 ICortexTool (C#)           Unified interface every tool implements
      |
 CortexSession (C#)         Shared state, session store, locale, capabilities
      |
 CortexResult<T> (C#)       Typed response envelope with structured error codes
      |
 DocumentAnalyzer (C#)      Scans the active document to populate DocumentCapabilities
```

### Key types

- **`CortexResult<T>`** -- Every tool returns `CortexResult<T>.Ok(data)` or `CortexResult<T>.Fail(code, message)`. Error codes are defined in `CortexErrorCode` (ElementNotFound, InvalidInput, TransactionFailed, etc.).
- **`ICortexTool`** -- Interface with `Name`, `Category`, `RequiresDocument`, `IsDynamic`, and `Execute(JObject, CortexSession)`.
- **`CortexSession`** -- Facade passed to every tool. Holds `ISessionStore`, `DocumentCapabilities`, and `DetectedLocale`.
- **`DocumentCapabilities`** -- Discovered at document open. Tracks present categories, shared parameters, worksets, phases, design options, and linked models.
- **`DocumentAnalyzer`** -- Implements `IDocumentAnalyzer`. Scans the Revit document and populates `DocumentCapabilities`, enabling/disabling dynamic tools.

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
      Meta/                     SayHelloTool (starter tool)
    RevitCortex.Tests/        Unit tests (xUnit)
      Discovery/                DocumentCapabilitiesTests
      Results/                  CortexResultTests
      Router/                   CortexRouterTests, FakeTool, FakeAnalyzer
      Session/                  SessionStoreTests
  server/                     TypeScript MCP server
    src/
      connection/               TCP connection to Plugin
      logging/                  Structured logging
      schemas/                  Zod schemas
      tools/                    Tool definitions
    index.ts                    Server entry point
    tsconfig.json
    package.json
```

## Building

### C# Plugin

From the repository root, specify the Revit version via the build configuration:

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2025
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2024
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2023
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2026
```

### TypeScript Server

```bash
cd server
npm install
npm run build
```

## Running Tests

```bash
dotnet test -c "Debug R25"
```

## Author

Luigi Dattilo
