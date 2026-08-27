# New tool checklist

## Files

- `src/RiveTT.Tools/<Category>/<ToolName>Tool.cs`: `ICortexTool` implementation.
- `src/RiveTT.Server/Tools/<Category>Tools.cs`: typed MCP wrapper.
- `src/RiveTT.Tests`: unit, contract, or source tests.
- `docs/utilisation/USER_GUIDE.md` and the relevant operator reference when behavior is user-facing.

## Requirements

- MCP name is `snake_case`; C# class is `PascalCaseTool`.
- Validate input before touching the document.
- Use `[ToolSafety]` with accurate read/write and destructive metadata.
- Execute Revit API work through the plugin dispatcher.
- Wrap writes in a transaction and surface failed commits as structured errors.
- Keep `dryRun` preview behavior when relevant.
- Return `CortexResult<object>.Ok(...)` or `.Fail(...)`; do not leak exceptions.
- Add the matching `[McpServerTool]` wrapper and tests.

## Verification

    dotnet test src/RiveTT.Tests/RiveTT.Tests.csproj -c Release
    dotnet build RiveTT.sln -c Release

The only supported target is Revit 2027 / .NET 10 / x64.
