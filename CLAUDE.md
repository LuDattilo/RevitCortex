# RevitCortex — AI Assistant Guide

## Project Overview

RevitCortex is a next-generation MCP (Model Context Protocol) server for Autodesk Revit. It improves on the original mcp-servers-for-revit with typed errors, session state, and dynamic tool discovery. Tools are rewritten from scratch — not copied from the fork reference.

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
├── RevitCortex.sln
├── src/
│   ├── RevitCortex.Core/       # Shared types: CortexResult<T>, ICortexTool, CortexSession, DocumentCapabilities
│   ├── RevitCortex.Plugin/     # Revit add-in (ExternalApplication, command routing)
│   └── RevitCortex.Tools/      # Tool implementations (each implements ICortexTool)
├── server/                     # TypeScript MCP server (stdio transport)
├── nuget.config
└── CLAUDE.md
```

## Architecture: Layer Cake

- **CortexResult\<T\>** — Every tool returns a typed result with success/error discriminator and structured error codes. No raw strings or exceptions leaking to the caller.
- **ICortexTool** — Interface all tools implement. Defines `Name`, `Description`, `Schema`, `IsDynamic`, and `ExecuteAsync(CortexSession, JsonElement)`.
- **CortexSession** — Shared state passed to every tool: active document, undo scope, user preferences, cancellation token.
- **DocumentCapabilities** — Discovered at document open. Describes what the active document supports (e.g., has structural elements, has MEP, has rooms). Tools with `IsDynamic = true` are only visible when the document has matching capabilities.

## Build Commands

### C# (from repo root)
```bash
dotnet build -c "Debug R25"      # Revit 2025, net8.0
dotnet build -c "Debug R24"      # Revit 2024, net48
dotnet build -c "Debug R23"      # Revit 2023, net48
dotnet build -c "Release R25"    # Release build for R25
```

### TypeScript MCP Server
```bash
cd server && npm run build
```

### Tests
```bash
dotnet test
```

## Key Patterns

1. **Every tool implements ICortexTool** and returns `CortexResult<T>`.
2. **Every tool receives CortexSession** — never access Revit globals directly.
3. **IsDynamic convention**: Tools with `IsDynamic = true` are only registered/visible when the active document has matching `DocumentCapabilities`. This keeps the tool list relevant and avoids errors from calling tools on incompatible documents.
4. **Typed errors**: Use `CortexError` with error codes (e.g., `ElementNotFound`, `InvalidParameter`, `TransactionFailed`) instead of throwing exceptions or returning error strings.

## Fork Reference

The original MCP server lives at:
```
C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit
```
This is **read-only reference material**. Tools are rewritten from scratch using the new architecture — never copy code directly.

## Revit Locale Detection

Revit localizes category and parameter names based on installation language. Detect the active locale from tool responses (parameter names reveal the language) rather than assuming one language.

**Always prefer OST_\* codes** (e.g., `OST_StructuralFraming`, `OST_Walls`) for category references — these are language-independent and work in any locale.

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
