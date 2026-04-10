# analyze_journal Tool — Design Spec

## Overview

A new MCP tool `analyze_journal` that parses Revit journal files and returns structured analysis. Runs entirely in TypeScript (local processing, no Revit connection required). Can analyze current or historical sessions.

## Architecture

100% TypeScript, local file processing. No C# plugin changes needed.

```
User -> MCP tool "analyze_journal" -> journal/locator.ts (find files)
                                   -> journal/parser.ts  (parse lines)
                                   -> journal/analyzers.ts (extract data)
                                   -> toolResponse (return structured JSON)
```

### File Structure

```
server/src/
  journal/
    parser.ts        — Line-by-line parser, classifies into typed JournalLine objects
    analyzers.ts     — Analysis functions for each analysis_type
    locator.ts       — Finds journal files on disk by Revit version
  schemas/
    journal.ts       — Zod input schema
  tools/
    analyze_journal.ts — MCP tool registration
```

## Input Schema

```typescript
AnalyzeJournalInput = z.object({
  analysis_type: z.enum([
    "summary",
    "session_diagnostics",
    "memory_profile",
    "command_usage",
    "addin_audit",
    "transaction_log",
    "full"
  ]).describe("Type of analysis to perform"),

  journal_path: z.string().optional()
    .describe("Explicit path to journal file. If omitted, uses most recent from standard folder"),

  revit_version: z.enum(["2023", "2024", "2025", "2026"]).optional().default("2025")
    .describe("Revit version to locate journal folder. Ignored if journal_path is provided"),

  last_n_sessions: z.number().int().min(1).max(20).optional().default(1)
    .describe("Number of recent journal sessions to analyze (default 1)")
})
```

## Journal Locator (`locator.ts`)

Finds journal files at:
```
%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <VERSION>\Journals\journal.NNNN.txt
```

- Lists all `journal.*.txt` files (excluding `.abbrev` and `.worker*.log`)
- Sorts by modification time descending (most recent first)
- Returns the requested `last_n_sessions` file paths
- If `journal_path` is provided, uses that directly

## Journal Parser (`parser.ts`)

Reads journal file line-by-line and classifies each into a typed `JournalLine`:

### Line Types

| Type | Detection | Data Extracted |
|------|-----------|----------------|
| `timestamp` | `'C`, `'E`, `'H` prefix + date pattern | datetime, type (C/E/H), message |
| `command` | `Jrn.Command` | scope ("Internal"/"Ribbon"/etc), command name, ID |
| `ribbon_event` | `Jrn.RibbonEvent` | event type, tab/panel name |
| `data` | `Jrn.Data` | key, value (e.g., "Transaction Successful", "Wall") |
| `directive` | `Jrn.Directive` | directive name, values |
| `dialog` | `Jrn.PushButton`, `Jrn.CheckBox`, `Jrn.Edit` | dialog name, control, value |
| `memory` | `Delta VM:` or `RAM Statistics:` | vmAvail, vmUsed, vmPeak, ramAvail, ramUsed, ramPeak |
| `gdi` | `GUI Resource Usage GDI:` | gdiAvail, gdiUsed, userUsed |
| `api_message` | `API_SUCCESS` or `API_ERROR` | level, message |
| `addin_manifest` | `[Jrn.AddInManifest]` | name, guid, class, vendor, loadTime, signed |
| `basic_file_info` | `[Jrn.BasicFileInfo]` | filename, path, worksharing, user, central, build, locale |
| `document_close` | `[Jrn.CloseDocumentFile]` | document info |
| `external_event` | `[Jrn.ExternalEventExecution]` | handler info |
| `performance` | `N:<<operation` pattern | elapsed seconds, thread, operation |
| `comment` | `'` (other comments) | raw text |
| `other` | anything else | raw text |

### Multi-line Handling

Lines ending with `_` (VBScript continuation) are joined with the next line before classification.

### Output

```typescript
interface ParsedJournal {
  filePath: string;
  lines: JournalLine[];
  header: {
    revitVersion: string;
    build: string;
    branch: string;
    startTime: Date;
  };
}
```

## Analyzers (`analyzers.ts`)

Each analysis_type maps to a function that takes `ParsedJournal` and returns structured data.

### `summary`

Overview of the session:
```typescript
{
  session: { start, end, durationMinutes, revitVersion, build },
  documents: [{ name, path, openedAt, closedAt, worksharing, user }],
  commandsTop10: [{ name, count }],
  errorSummary: { apiErrors, transactionFailures, crashDetected },
  memory: { peakRAM_MB, peakVM_MB, gdiPeak },
  addins: { loaded, failed, slowestLoadMs, slowestName }
}
```

### `session_diagnostics`

Problems and anomalies:
```typescript
{
  crashDetected: boolean,
  abnormalTermination: boolean,  // journal ends without Jrn.Command "Quit"
  apiErrors: [{ timestamp, message }],
  transactionFailures: [{ timestamp, transactionName, message }],
  warnings: [{ timestamp, message }],
  sessionDuration: string,
  lastCommand: { timestamp, name }
}
```

### `memory_profile`

Memory timeline and leak detection:
```typescript
{
  checkpoints: [{ timestamp, vmUsed_MB, vmPeak_MB, ramUsed_MB, ramPeak_MB, gdiUsed }],
  trend: "stable" | "increasing" | "spike",
  peakVM_MB: number,
  peakRAM_MB: number,
  gdiPeak: number,
  estimatedLeakRate_MB_per_hour: number | null
}
```

### `command_usage`

Command frequency and patterns:
```typescript
{
  totalCommands: number,
  uniqueCommands: number,
  commands: [{ name, count, firstUsed, lastUsed }],  // sorted by count desc
  ribbonEvents: [{ tab, count }],
  timeline: [{ hour, commandCount }]  // hourly distribution
}
```

### `addin_audit`

Add-in health:
```typescript
{
  addins: [{
    name, vendor, version, guid,
    loadTimeMs, signed,
    apiErrors: number,
    externalEvents: number
  }],
  totalLoadTimeMs: number,
  failedLoads: [{ name, error }],
  externalEventExecutions: [{ handler, count, timestamps }]
}
```

### `transaction_log`

Model change log:
```typescript
{
  transactions: [{
    timestamp, name, result: "success" | "failed" | "rolled_back",
    durationMs: number | null
  }],
  successCount: number,
  failureCount: number,
  rollbackCount: number
}
```

### `full`

Returns all of the above sections combined in a single response.

## C# Side

No C# tool implementation needed. The parser runs entirely in TypeScript reading files from disk.

Future enhancement: a small `get_journal_path` C# tool that returns `Application.RecordingJournalFilename` to identify the live journal. Not in scope for v1.

## Error Handling

- Journal file not found → clear error with path attempted
- Permission denied → suggest running as admin or copying file
- Malformed lines → skip with warning count in response
- Empty journal → return structure with zero counts
- File too large (>50MB) → warn and process first 100,000 lines

## Registration

Add to `server/src/tools/register.ts`:
```typescript
{ name: "analyze_journal", register: registerAnalyzeJournalTool }
```

## C# ICortexTool

Not needed — this is a TypeScript-only local tool. No C# `ICortexTool` implementation required.
