# Token Usage Tracking & Cost Reports

**Date:** 2026-04-10
**Status:** Approved

## Goal

Track token consumption across both MCP tool calls (TypeScript server) and direct Anthropic API calls (C# CortexChatClient) to generate daily, weekly, and monthly reports showing which tasks consume the most tokens and estimating API costs for enterprise consumption-based pricing.

## Architecture

```
MCP Tools (TS)  ──→  ~/.revitcortex/usage-mcp.db     ──┐
                     (sql.js, same infrastructure)       │
                                                         ├──→  report_token_usage (MCP tool)
CortexChatClient (C#) ──→  ~/.revitcortex/usage.jsonl  ──┘     ├─ unified query
                           (append-only, one record             ├─ day/week/month aggregations
                            per API call with real usage)       ├─ estimated costs
                                                                └─ CSV export
```

Two separate storage backends to avoid file lock conflicts:
- **TS server**: SQLite via sql.js (same pattern as existing `revitcortex-data.db`)
- **C# plugin**: Append-only JSONL (no SQLite dependency needed in C#)

The `report_token_usage` MCP tool reads from both sources and unifies them.

## Schema: MCP Usage DB (TypeScript - `usage-mcp.db`)

```sql
CREATE TABLE token_usage (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  timestamp TEXT NOT NULL,          -- ISO8601
  tool_name TEXT NOT NULL,
  tool_category TEXT,               -- "Elements", "Views", "Schedules", etc.
  session_id TEXT,                  -- conversation grouping
  duration_ms INTEGER,
  response_chars INTEGER,
  estimated_tokens INTEGER,
  source TEXT DEFAULT 'estimated',  -- 'estimated' | 'actual'
  is_error INTEGER DEFAULT 0,
  model TEXT
);
CREATE INDEX idx_usage_timestamp ON token_usage(timestamp);
CREATE INDEX idx_usage_tool ON token_usage(tool_name);
CREATE INDEX idx_usage_session ON token_usage(session_id);
```

## Schema: API Usage JSONL (C# - `usage.jsonl`)

One line per Anthropic API call:

```json
{
  "timestamp": "2026-04-10T16:30:00Z",
  "session_id": "20260410-a3f2",
  "model": "claude-sonnet-4-6",
  "input_tokens": 1523,
  "output_tokens": 847,
  "thinking_tokens": 200,
  "tool_calls": ["get_element_parameters", "ai_element_filter"],
  "source": "actual",
  "duration_ms": 3200
}
```

## Pricing Configuration

Stored in `~/.revitcortex/settings.json` alongside existing settings:

```json
{
  "tokenPricing": {
    "claude-sonnet-4-6": { "inputPerMTok": 3.0, "outputPerMTok": 15.0 },
    "claude-haiku-4-5": { "inputPerMTok": 0.80, "outputPerMTok": 4.0 },
    "claude-opus-4-6": { "inputPerMTok": 15.0, "outputPerMTok": 75.0 }
  }
}
```

Editable from the Settings window (new Pricing tab).

## Session ID

Generated at:
- **TS server**: On MCP server startup. Format: `{YYYYMMDD}-{random4hex}` (e.g., `20260410-a3f2`)
- **C# plugin**: On CortexPanel open or first API call per Revit session. Same format.

Allows grouping all calls within a conversation/session for per-session cost analysis.

## Components

### TypeScript Server

| Component | File | Description |
|-----------|------|-------------|
| Usage DB module | `server/src/database/usageDb.ts` | Init SQLite, `recordUsage()`, query/aggregation functions |
| Update compactTool | `server/src/logging/compactTool.ts` | Write to SQLite instead of JSONL, add session_id + category |
| Update tokenLogger | `server/src/logging/tokenLogger.ts` | Migrate from JSONL append to SQLite insert |
| Session module | `server/src/logging/session.ts` | Generate and hold session_id for server lifetime |
| Tool category map | `server/src/logging/toolCategories.ts` | Map tool_name -> category (derived from register.ts structure) |
| Report tool | `server/src/tools/report_token_usage.ts` | MCP tool: query, aggregate, export |
| Report schema | `server/src/schemas/usage.ts` | Zod schema for report_token_usage input |
| JSONL reader | `server/src/database/usageJsonl.ts` | Read and parse C# usage.jsonl for unified reports |
| Registration | `server/src/tools/register.ts` | Register report_token_usage tool |

### C# Plugin

| Component | File | Description |
|-----------|------|-------------|
| Usage tracker | `src/RevitCortex.Plugin/Tracking/UsageTracker.cs` | Append JSONL, read for UI |
| Update CortexChatClient | `src/RevitCortex.Plugin/UI/CortexChatClient.cs` | Extract `usage` from API response, call UsageTracker |
| Pricing settings | `src/RevitCortex.Plugin/UI/PricingSettingsPage.xaml` | UI for editing per-model pricing |
| Usage report page | `src/RevitCortex.Plugin/UI/UsageReportPage.xaml` | Tab in settings showing usage summary |

## MCP Tool: `report_token_usage`

### Input

```typescript
{
  period: "day" | "week" | "month" | "custom",
  startDate?: string,         // for "custom", ISO date
  endDate?: string,           // for "custom", ISO date
  groupBy: "tool" | "category" | "session" | "day",
  includeApiCalls: boolean,   // merge C# JSONL data
  exportCsv?: boolean         // generate CSV file
}
```

### Output

```json
{
  "period": "2026-04-01 to 2026-04-10",
  "summary": {
    "totalInputTokens": 125000,
    "totalOutputTokens": 48000,
    "estimatedCost_USD": 1.095,
    "totalCalls": 342,
    "sources": { "actual": 89, "estimated": 253 }
  },
  "breakdown": [
    { "key": "ai_element_filter", "calls": 45, "tokens": 23000, "cost_USD": 0.12 },
    { "key": "get_element_parameters", "calls": 89, "tokens": 18000, "cost_USD": 0.09 }
  ],
  "csvPath": "~/.revitcortex/reports/usage-2026-04.csv"
}
```

### CSV Export Format

```csv
timestamp,tool_name,category,session_id,input_tokens,output_tokens,estimated_tokens,source,model,duration_ms,cost_usd
2026-04-10T14:30:00Z,ai_element_filter,Elements,20260410-a3f2,,,580,estimated,,120,0.002
2026-04-10T14:30:05Z,,API Call,20260410-b1c3,1523,847,,actual,claude-sonnet-4-6,3200,0.017
```

## Data Flow

### MCP Tool Call (TypeScript)

1. Tool executes, calls `toolResponse()` or `toolError()`
2. `compactTool.ts` calculates response chars and estimated tokens
3. Calls `recordUsage()` in `usageDb.ts` with tool_name, category, session_id, estimated_tokens, source="estimated"
4. Row inserted into `usage-mcp.db`

### API Call (C# CortexChatClient)

1. `CortexChatClient` sends request to Anthropic API
2. Response JSON contains `usage: { input_tokens, output_tokens }`
3. `CortexChatClient` extracts usage and calls `UsageTracker.Record()`
4. `UsageTracker` appends one JSONL line to `~/.revitcortex/usage.jsonl`

### Report Generation

1. User calls `report_token_usage` tool (or opens Usage Report tab in Revit)
2. Tool queries `usage-mcp.db` for MCP data
3. If `includeApiCalls=true`, reads and parses `usage.jsonl` for API data
4. Merges, aggregates by requested groupBy dimension
5. Applies pricing from settings.json to calculate costs
6. Returns JSON report and optionally writes CSV to `~/.revitcortex/reports/`

## UI: Usage Report Page (C# - Revit Settings)

Minimal tab in SettingsWindow showing:
- Period selector (day/week/month)
- Total tokens + estimated cost
- Top 10 tools by consumption
- Export CSV button

Reads directly from `usage.jsonl` for API data. Does not need to read the TS SQLite DB (that data is accessible via the MCP tool).

## UI: Pricing Settings Page (C# - Revit Settings)

DataGrid with columns: Model, Input $/MTok, Output $/MTok. Editable. Saved to `settings.json`.

## Migration

- Existing `token-usage.jsonl` data is not migrated (low value, estimated only)
- The file continues to exist but `tokenLogger.ts` switches to writing to SQLite
- Old JSONL file can be deleted manually

## Error Handling

- DB write failures are silent (logging must never break tool execution)
- Missing `usage.jsonl` file: report tool returns MCP-only data with a note
- Missing pricing config: use hardcoded defaults for current models
- Corrupted JSONL lines: skip and log warning
