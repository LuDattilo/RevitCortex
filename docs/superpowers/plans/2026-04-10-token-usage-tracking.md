# Token Usage Tracking & Cost Reports — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track token consumption across MCP tool calls (TS) and Anthropic API calls (C#), store persistently, and generate cost reports by period with CSV export.

**Architecture:** Two storage backends — SQLite in the TS server for MCP tool usage, append-only JSONL from C# for API usage. A unified `report_token_usage` MCP tool reads both sources, applies pricing, and generates aggregated reports. A session ID ties calls to conversations.

**Tech Stack:** TypeScript (sql.js, Zod), C# (.NET, Newtonsoft.Json), SQLite, JSONL

**Spec:** `docs/superpowers/specs/2026-04-10-token-usage-tracking-design.md`

---

## File Map

| # | File | Action | Responsibility |
|---|------|--------|----------------|
| 1 | `server/src/logging/session.ts` | Create | Generate and hold session_id for server lifetime |
| 2 | `server/src/logging/toolCategories.ts` | Create | Map tool_name → category string |
| 3 | `server/src/database/usageDb.ts` | Create | SQLite schema, `recordUsage()`, query/aggregation functions |
| 4 | `server/src/database/usageJsonl.ts` | Create | Read and parse C# `usage.jsonl` for unified reports |
| 5 | `server/src/logging/tokenLogger.ts` | Modify | Switch from JSONL append to SQLite insert via usageDb |
| 6 | `server/src/logging/compactTool.ts` | Modify | Pass session_id + category to tokenLogger |
| 7 | `server/src/schemas/usage.ts` | Create | Zod schema for `report_token_usage` input |
| 8 | `server/src/tools/report_token_usage.ts` | Create | MCP tool: query, aggregate, cost calculation, CSV export |
| 9 | `server/src/tools/register.ts` | Modify | Add import + registration for report_token_usage |
| 10 | `src/RevitCortex.Plugin/Tracking/UsageTracker.cs` | Create | Append JSONL, read for UI, manage session_id |
| 11 | `src/RevitCortex.Plugin/UI/CortexChatClient.cs` | Modify | Extract `usage` from API response, call UsageTracker |

---

### Task 1: Session ID Module

**Files:**
- Create: `server/src/logging/session.ts`

- [ ] **Step 1: Create session module**

```typescript
// server/src/logging/session.ts
import { randomBytes } from "crypto";

const SESSION_ID = `${formatDate(new Date())}-${randomBytes(2).toString("hex")}`;

function formatDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}${m}${day}`;
}

export function getSessionId(): string {
  return SESSION_ID;
}
```

- [ ] **Step 2: Build to verify no errors**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 3: Commit**

```bash
git add server/src/logging/session.ts
git commit -m "feat(usage): add session ID module for tracking"
```

---

### Task 2: Tool Categories Map

**Files:**
- Create: `server/src/logging/toolCategories.ts`

- [ ] **Step 1: Create category map**

Build the map from the registration array in `register.ts` (lines 127–251). Each tool maps to one of these categories: `Elements`, `Views`, `Sheets`, `Schedules`, `Parameters`, `Project`, `Materials`, `Creation`, `Export`, `Audit`, `Workflows`, `Database`, `Journal`, `Meta`.

```typescript
// server/src/logging/toolCategories.ts

const TOOL_CATEGORIES: Record<string, string> = {
  // Elements
  get_element_parameters: "Elements",
  ai_element_filter: "Elements",
  set_element_parameters: "Elements",
  get_selected_elements: "Elements",
  get_current_view_elements: "Elements",
  get_linked_elements: "Elements",
  get_elements_in_spatial_volume: "Elements",
  delete_element: "Elements",
  operate_element: "Elements",
  change_element_type: "Elements",
  modify_element: "Elements",
  copy_elements: "Elements",
  measure_between_elements: "Elements",
  renumber_elements: "Elements",
  find_untagged_elements: "Elements",
  find_undimensioned_elements: "Elements",
  export_elements_data: "Elements",
  match_element_properties: "Elements",
  color_elements: "Elements",
  delete_selection: "Elements",
  save_selection: "Elements",
  load_selection: "Elements",
  set_element_phase: "Elements",
  set_element_workset: "Elements",

  // Creation
  create_line_based_element: "Creation",
  create_point_based_element: "Creation",
  create_surface_based_element: "Creation",
  create_floor: "Creation",
  create_grid: "Creation",
  create_level: "Creation",
  create_room: "Creation",
  create_array: "Creation",
  create_filled_region: "Creation",
  create_dimensions: "Creation",
  create_text_note: "Creation",
  create_color_legend: "Creation",
  create_structural_framing_system: "Creation",

  // Views
  create_view: "Views",
  duplicate_view: "Views",
  create_view_filter: "Views",
  override_graphics: "Views",
  apply_view_template: "Views",
  batch_modify_view_range: "Views",
  section_box_from_selection: "Views",
  manage_unplaced_views: "Views",
  manage_view_templates: "Views",
  create_views_from_rooms: "Views",
  get_current_view_info: "Views",
  rename_views: "Views",
  lines_per_view_count: "Views",

  // Sheets
  create_sheet: "Sheets",
  place_viewport: "Sheets",
  align_viewports: "Sheets",
  batch_create_sheets: "Sheets",
  create_placeholder_sheets: "Sheets",
  duplicate_sheet_with_content: "Sheets",
  duplicate_sheet_with_views: "Sheets",

  // Schedules
  create_schedule: "Schedules",
  create_preset_schedule: "Schedules",
  get_schedule_data: "Schedules",
  delete_schedule: "Schedules",
  duplicate_schedule: "Schedules",
  modify_schedule: "Schedules",
  list_schedulable_fields: "Schedules",
  import_table: "Schedules",

  // Parameters
  add_shared_parameter: "Parameters",
  manage_project_parameters: "Parameters",
  add_prefix_suffix: "Parameters",
  get_shared_parameters: "Parameters",
  bulk_modify_parameter_values: "Parameters",
  clear_parameter_values: "Parameters",
  transfer_parameters: "Parameters",
  filter_by_parameter_value: "Parameters",
  batch_rename: "Parameters",
  sync_csv_parameters: "Parameters",

  // Project
  get_project_info: "Project",
  get_phases: "Project",
  get_worksets: "Project",
  get_warnings: "Project",
  create_revision: "Project",
  manage_links: "Project",
  load_family: "Project",
  rename_families: "Project",
  get_available_family_types: "Project",
  list_family_sizes: "Project",
  get_room_openings: "Project",
  tag_rooms: "Project",
  tag_walls: "Project",
  duplicate_system_type: "Project",

  // Materials
  get_materials: "Materials",
  get_material_properties: "Materials",
  get_material_quantities: "Materials",
  set_material_properties: "Materials",
  create_material: "Materials",
  duplicate_material: "Materials",
  delete_material: "Materials",
  get_compound_structure: "Materials",
  set_compound_structure: "Materials",

  // Export
  export_room_data: "Export",
  export_schedule: "Export",
  export_families: "Export",
  export_shared_parameter_file: "Export",
  batch_export: "Export",
  export_to_excel: "Export",
  import_from_excel: "Export",

  // Audit
  analyze_model_statistics: "Audit",
  check_model_health: "Audit",
  audit_families: "Audit",
  purge_unused: "Audit",
  cad_link_cleanup: "Audit",
  clash_detection: "Audit",
  wipe_empty_tags: "Audit",

  // Workflows
  workflow_clash_review: "Workflows",
  workflow_room_documentation: "Workflows",
  workflow_sheet_set: "Workflows",
  workflow_model_audit: "Workflows",
  workflow_data_roundtrip: "Workflows",

  // Database
  store_project_data: "Database",
  store_room_data: "Database",
  query_stored_data: "Database",

  // Journal
  analyze_journal: "Journal",

  // Code
  send_code_to_revit: "Code",

  // Meta
  say_hello: "Meta",
  report_token_usage: "Meta",
};

export function getToolCategory(toolName: string): string {
  return TOOL_CATEGORIES[toolName] ?? "Other";
}
```

- [ ] **Step 2: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 3: Commit**

```bash
git add server/src/logging/toolCategories.ts
git commit -m "feat(usage): add tool-to-category mapping"
```

---

### Task 3: Usage SQLite DB Module

**Files:**
- Create: `server/src/database/usageDb.ts`

- [ ] **Step 1: Create the usage database module**

Follow the exact pattern from `server/src/database/db.ts` (lines 1–132) but for a separate `usage-mcp.db` file.

```typescript
// server/src/database/usageDb.ts
import initSqlJs, { Database as SqlJsDatabase } from "sql.js";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import { homedir } from "os";
import { mkdirSync, readFileSync, writeFileSync } from "fs";

const __bundledir = dirname(fileURLToPath(import.meta.url));

const DB_DIR = join(homedir(), ".revitcortex");
mkdirSync(DB_DIR, { recursive: true });
const DB_PATH = join(DB_DIR, "usage-mcp.db");

let db: SqlJsDatabase | null = null;
let savePending = false;

function scheduleSave() {
  if (savePending) return;
  savePending = true;
  setImmediate(() => {
    savePending = false;
    if (db) writeFileSync(DB_PATH, Buffer.from(db.export()));
  });
}

function flushDatabase() {
  savePending = false;
  if (db) writeFileSync(DB_PATH, Buffer.from(db.export()));
}

export async function getUsageDatabase(): Promise<SqlJsDatabase> {
  if (db) return db;

  const SQL = await initSqlJs({
    locateFile: (file: string) => join(__bundledir, file),
  });

  try {
    const fileBuffer = readFileSync(DB_PATH);
    db = new SQL.Database(fileBuffer);
  } catch {
    db = new SQL.Database();
  }

  initializeUsageSchema(db);
  return db;
}

function initializeUsageSchema(database: SqlJsDatabase) {
  database.run(`
    CREATE TABLE IF NOT EXISTS token_usage (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      timestamp TEXT NOT NULL,
      tool_name TEXT NOT NULL,
      tool_category TEXT,
      session_id TEXT,
      duration_ms INTEGER,
      response_chars INTEGER,
      estimated_tokens INTEGER,
      source TEXT DEFAULT 'estimated',
      is_error INTEGER DEFAULT 0,
      model TEXT
    )
  `);
  database.run("CREATE INDEX IF NOT EXISTS idx_usage_timestamp ON token_usage(timestamp)");
  database.run("CREATE INDEX IF NOT EXISTS idx_usage_tool ON token_usage(tool_name)");
  database.run("CREATE INDEX IF NOT EXISTS idx_usage_session ON token_usage(session_id)");
}

function getDb(): SqlJsDatabase {
  if (!db) throw new Error("Usage database not initialized. Call getUsageDatabase() first.");
  return db;
}

export interface UsageRecord {
  toolName: string;
  toolCategory: string;
  sessionId: string;
  durationMs: number;
  responseChars: number;
  estimatedTokens: number;
  source: "estimated" | "actual";
  isError: boolean;
  model?: string;
}

export function recordUsage(record: UsageRecord): void {
  try {
    getDb().run(
      `INSERT INTO token_usage (timestamp, tool_name, tool_category, session_id, duration_ms, response_chars, estimated_tokens, source, is_error, model)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      [
        new Date().toISOString(),
        record.toolName,
        record.toolCategory,
        record.sessionId,
        record.durationMs,
        record.responseChars,
        record.estimatedTokens,
        record.source,
        record.isError ? 1 : 0,
        record.model ?? null,
      ]
    );
    scheduleSave();
  } catch {
    // Silent fail — logging must never break tool execution
  }
}

export interface UsageQueryParams {
  startDate?: string;
  endDate?: string;
  toolName?: string;
  category?: string;
  sessionId?: string;
}

export function queryUsage(params: UsageQueryParams): any[] {
  const conditions: string[] = [];
  const values: any[] = [];

  if (params.startDate) { conditions.push("timestamp >= ?"); values.push(params.startDate); }
  if (params.endDate) { conditions.push("timestamp <= ?"); values.push(params.endDate); }
  if (params.toolName) { conditions.push("tool_name = ?"); values.push(params.toolName); }
  if (params.category) { conditions.push("tool_category = ?"); values.push(params.category); }
  if (params.sessionId) { conditions.push("session_id = ?"); values.push(params.sessionId); }

  const where = conditions.length > 0 ? `WHERE ${conditions.join(" AND ")}` : "";
  const sql = `SELECT * FROM token_usage ${where} ORDER BY timestamp DESC`;

  const results: any[] = [];
  const stmt = getDb().prepare(sql);
  if (values.length > 0) stmt.bind(values);
  while (stmt.step()) results.push(stmt.getAsObject());
  stmt.free();
  return results;
}

export function aggregateUsage(
  params: UsageQueryParams,
  groupBy: "tool_name" | "tool_category" | "session_id" | "date"
): any[] {
  const conditions: string[] = [];
  const values: any[] = [];

  if (params.startDate) { conditions.push("timestamp >= ?"); values.push(params.startDate); }
  if (params.endDate) { conditions.push("timestamp <= ?"); values.push(params.endDate); }

  const where = conditions.length > 0 ? `WHERE ${conditions.join(" AND ")}` : "";
  const groupCol = groupBy === "date" ? "date(timestamp)" : groupBy;
  const selectCol = groupBy === "date" ? "date(timestamp) as group_key" : `${groupBy} as group_key`;

  const sql = `
    SELECT ${selectCol},
           COUNT(*) as calls,
           SUM(estimated_tokens) as total_tokens,
           SUM(response_chars) as total_chars,
           SUM(CASE WHEN is_error = 1 THEN 1 ELSE 0 END) as errors,
           AVG(duration_ms) as avg_duration_ms
    FROM token_usage ${where}
    GROUP BY ${groupCol}
    ORDER BY total_tokens DESC
  `;

  const results: any[] = [];
  const stmt = getDb().prepare(sql);
  if (values.length > 0) stmt.bind(values);
  while (stmt.step()) results.push(stmt.getAsObject());
  stmt.free();
  return results;
}

function cleanup() {
  if (db) {
    flushDatabase();
    db.close();
  }
}
process.on("exit", cleanup);
process.on("SIGTERM", () => { cleanup(); process.exit(0); });
process.on("SIGINT", () => { cleanup(); process.exit(0); });
```

- [ ] **Step 2: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 3: Commit**

```bash
git add server/src/database/usageDb.ts
git commit -m "feat(usage): add SQLite usage database module"
```

---

### Task 4: Migrate tokenLogger to SQLite

**Files:**
- Modify: `server/src/logging/tokenLogger.ts`
- Modify: `server/src/logging/compactTool.ts`

- [ ] **Step 1: Rewrite tokenLogger.ts to use SQLite + session + category**

Replace the entire file contents:

```typescript
// server/src/logging/tokenLogger.ts
import { recordUsage, getUsageDatabase } from "../database/usageDb.js";
import { getSessionId } from "./session.js";
import { getToolCategory } from "./toolCategories.js";

let initialized = false;

async function ensureInit() {
  if (initialized) return;
  try {
    await getUsageDatabase();
    initialized = true;
  } catch {
    // Silent fail
  }
}

// Fire-and-forget init on first import
ensureInit();

/**
 * Log a tool response for token usage analysis.
 * Writes to ~/.revitcortex/usage-mcp.db via usageDb.
 */
export function logTokenUsage(
  toolName: string,
  responseText: string,
  durationMs: number,
  isError: boolean = false
): void {
  const chars = responseText.length;
  const estimatedTokens = Math.ceil(chars / 4);

  recordUsage({
    toolName,
    toolCategory: getToolCategory(toolName),
    sessionId: getSessionId(),
    durationMs,
    responseChars: chars,
    estimatedTokens,
    source: "estimated",
    isError,
  });
}
```

- [ ] **Step 2: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

`compactTool.ts` does NOT need changes — it already calls `logTokenUsage()` which now writes to SQLite instead of JSONL. The session_id and category are injected inside `logTokenUsage`.

- [ ] **Step 3: Commit**

```bash
git add server/src/logging/tokenLogger.ts
git commit -m "feat(usage): migrate tokenLogger from JSONL to SQLite"
```

---

### Task 5: JSONL Reader for C# API Usage

**Files:**
- Create: `server/src/database/usageJsonl.ts`

- [ ] **Step 1: Create the JSONL reader**

```typescript
// server/src/database/usageJsonl.ts
import { readFileSync, existsSync } from "fs";
import { join } from "path";
import { homedir } from "os";

const JSONL_PATH = join(homedir(), ".revitcortex", "usage.jsonl");

export interface ApiUsageEntry {
  timestamp: string;
  session_id?: string;
  model?: string;
  input_tokens: number;
  output_tokens: number;
  thinking_tokens?: number;
  tool_calls?: string[];
  source: "actual";
  duration_ms?: number;
}

export function readApiUsage(startDate?: string, endDate?: string): ApiUsageEntry[] {
  if (!existsSync(JSONL_PATH)) return [];

  const entries: ApiUsageEntry[] = [];

  try {
    const content = readFileSync(JSONL_PATH, "utf-8");
    for (const line of content.split("\n")) {
      if (!line.trim()) continue;
      try {
        const entry = JSON.parse(line) as ApiUsageEntry;
        if (startDate && entry.timestamp < startDate) continue;
        if (endDate && entry.timestamp > endDate) continue;
        entries.push(entry);
      } catch {
        // Skip malformed lines
      }
    }
  } catch {
    // File read error — return empty
  }

  return entries;
}

export function aggregateApiUsage(
  entries: ApiUsageEntry[],
  groupBy: "tool" | "category" | "session" | "day"
): Array<{ group_key: string; calls: number; input_tokens: number; output_tokens: number; thinking_tokens: number }> {
  const map = new Map<string, { calls: number; input_tokens: number; output_tokens: number; thinking_tokens: number }>();

  for (const entry of entries) {
    let key: string;
    switch (groupBy) {
      case "tool":
        // One entry per tool in tool_calls array
        for (const tool of entry.tool_calls ?? ["api_call"]) {
          const existing = map.get(tool) ?? { calls: 0, input_tokens: 0, output_tokens: 0, thinking_tokens: 0 };
          existing.calls += 1;
          existing.input_tokens += entry.input_tokens;
          existing.output_tokens += entry.output_tokens;
          existing.thinking_tokens += entry.thinking_tokens ?? 0;
          map.set(tool, existing);
        }
        continue;
      case "session":
        key = entry.session_id ?? "unknown";
        break;
      case "day":
        key = entry.timestamp.slice(0, 10);
        break;
      case "category":
        key = "API Call";
        break;
      default:
        key = "unknown";
    }

    const existing = map.get(key) ?? { calls: 0, input_tokens: 0, output_tokens: 0, thinking_tokens: 0 };
    existing.calls += 1;
    existing.input_tokens += entry.input_tokens;
    existing.output_tokens += entry.output_tokens;
    existing.thinking_tokens += entry.thinking_tokens ?? 0;
    map.set(key, existing);
  }

  return Array.from(map.entries()).map(([group_key, data]) => ({ group_key, ...data }));
}
```

- [ ] **Step 2: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 3: Commit**

```bash
git add server/src/database/usageJsonl.ts
git commit -m "feat(usage): add JSONL reader for C# API usage data"
```

---

### Task 6: Zod Schema for report_token_usage

**Files:**
- Create: `server/src/schemas/usage.ts`

- [ ] **Step 1: Create the schema**

```typescript
// server/src/schemas/usage.ts
import { z } from "zod";

export const ReportTokenUsageInput = z.object({
  period: z.enum(["day", "week", "month", "custom"]).describe(
    "Time period for the report. 'day' = today, 'week' = last 7 days, 'month' = last 30 days, 'custom' = use startDate/endDate"
  ),
  startDate: z.string().optional().describe("Start date (ISO format, e.g. '2026-04-01') — required for 'custom' period"),
  endDate: z.string().optional().describe("End date (ISO format, e.g. '2026-04-10') — required for 'custom' period"),
  groupBy: z.enum(["tool", "category", "session", "day"]).default("tool").describe(
    "How to group results: by individual tool, by tool category, by session, or by day"
  ),
  includeApiCalls: z.boolean().default(true).describe("Include direct Anthropic API call data from the Revit chat panel"),
  exportCsv: z.boolean().default(false).describe("Generate a CSV file in ~/.revitcortex/reports/"),
});
```

- [ ] **Step 2: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 3: Commit**

```bash
git add server/src/schemas/usage.ts
git commit -m "feat(usage): add Zod schema for report_token_usage"
```

---

### Task 7: report_token_usage MCP Tool

**Files:**
- Create: `server/src/tools/report_token_usage.ts`
- Modify: `server/src/tools/register.ts`

- [ ] **Step 1: Create the report tool**

```typescript
// server/src/tools/report_token_usage.ts
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ReportTokenUsageInput } from "../schemas/usage.js";
import { toolResponse, toolError } from "../logging/compactTool.js";
import { getUsageDatabase, queryUsage, aggregateUsage } from "../database/usageDb.js";
import { readApiUsage, aggregateApiUsage } from "../database/usageJsonl.js";
import { mkdirSync, writeFileSync, readFileSync, existsSync } from "fs";
import { join } from "path";
import { homedir } from "os";

const SETTINGS_PATH = join(homedir(), ".revitcortex", "settings.json");
const REPORTS_DIR = join(homedir(), ".revitcortex", "reports");

interface ModelPricing {
  inputPerMTok: number;
  outputPerMTok: number;
}

const DEFAULT_PRICING: Record<string, ModelPricing> = {
  "claude-sonnet-4-6": { inputPerMTok: 3.0, outputPerMTok: 15.0 },
  "claude-haiku-4-5": { inputPerMTok: 0.80, outputPerMTok: 4.0 },
  "claude-opus-4-6": { inputPerMTok: 15.0, outputPerMTok: 75.0 },
};

function loadPricing(): Record<string, ModelPricing> {
  try {
    if (existsSync(SETTINGS_PATH)) {
      const settings = JSON.parse(readFileSync(SETTINGS_PATH, "utf-8"));
      if (settings.tokenPricing) return settings.tokenPricing;
    }
  } catch { /* use defaults */ }
  return DEFAULT_PRICING;
}

function computeDateRange(period: string, startDate?: string, endDate?: string): { start: string; end: string } {
  const now = new Date();
  const end = endDate ?? now.toISOString();

  switch (period) {
    case "day": {
      const start = new Date(now);
      start.setHours(0, 0, 0, 0);
      return { start: start.toISOString(), end };
    }
    case "week": {
      const start = new Date(now);
      start.setDate(start.getDate() - 7);
      return { start: start.toISOString(), end };
    }
    case "month": {
      const start = new Date(now);
      start.setDate(start.getDate() - 30);
      return { start: start.toISOString(), end };
    }
    case "custom":
      return { start: startDate ?? new Date(0).toISOString(), end };
    default:
      return { start: new Date(0).toISOString(), end };
  }
}

function estimateCost(tokens: number, model: string, direction: "input" | "output", pricing: Record<string, ModelPricing>): number {
  const p = pricing[model] ?? pricing["claude-sonnet-4-6"] ?? { inputPerMTok: 3.0, outputPerMTok: 15.0 };
  const rate = direction === "input" ? p.inputPerMTok : p.outputPerMTok;
  return (tokens / 1_000_000) * rate;
}

export function registerReportTokenUsageTool(server: McpServer): void {
  server.tool(
    "report_token_usage",
    "Generate token usage and cost reports. Shows which tools/categories consume the most tokens, with estimated API costs. Supports day/week/month/custom periods with CSV export.",
    ReportTokenUsageInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        await getUsageDatabase();
        const pricing = loadPricing();
        const range = computeDateRange(args.period, args.startDate, args.endDate);

        // MCP tool usage from SQLite
        const groupByCol = args.groupBy === "tool" ? "tool_name"
          : args.groupBy === "category" ? "tool_category"
          : args.groupBy === "session" ? "session_id"
          : "date";

        const mcpAgg = aggregateUsage({ startDate: range.start, endDate: range.end }, groupByCol as any);

        let totalEstimatedTokens = 0;
        let totalCalls = 0;
        let estimatedSourceCount = 0;

        const breakdown: Array<Record<string, unknown>> = [];

        for (const row of mcpAgg) {
          const tokens = Number(row.total_tokens) || 0;
          totalEstimatedTokens += tokens;
          totalCalls += Number(row.calls) || 0;
          estimatedSourceCount += Number(row.calls) || 0;
          // For estimated tokens, assume ~equal split input/output for cost estimation
          const cost = estimateCost(Math.ceil(tokens * 0.7), "claude-sonnet-4-6", "input", pricing)
                     + estimateCost(Math.ceil(tokens * 0.3), "claude-sonnet-4-6", "output", pricing);
          breakdown.push({
            key: row.group_key ?? "unknown",
            source: "estimated",
            calls: Number(row.calls) || 0,
            tokens,
            errors: Number(row.errors) || 0,
            avgDurationMs: Math.round(Number(row.avg_duration_ms) || 0),
            cost_USD: Math.round(cost * 1000) / 1000,
          });
        }

        // API usage from JSONL
        let totalInputTokens = 0;
        let totalOutputTokens = 0;
        let actualSourceCount = 0;
        let apiCost = 0;

        if (args.includeApiCalls) {
          const apiEntries = readApiUsage(range.start, range.end);
          actualSourceCount = apiEntries.length;

          if (apiEntries.length > 0) {
            const apiAgg = aggregateApiUsage(apiEntries, args.groupBy);
            for (const row of apiAgg) {
              totalInputTokens += row.input_tokens;
              totalOutputTokens += row.output_tokens;
              totalCalls += row.calls;
              const cost = estimateCost(row.input_tokens, "claude-sonnet-4-6", "input", pricing)
                         + estimateCost(row.output_tokens, "claude-sonnet-4-6", "output", pricing);
              apiCost += cost;
              breakdown.push({
                key: row.group_key,
                source: "actual",
                calls: row.calls,
                inputTokens: row.input_tokens,
                outputTokens: row.output_tokens,
                thinkingTokens: row.thinking_tokens,
                cost_USD: Math.round(cost * 1000) / 1000,
              });
            }
          }
        }

        // Sort breakdown by cost descending
        breakdown.sort((a, b) => ((b.cost_USD as number) || 0) - ((a.cost_USD as number) || 0));

        const totalCost = breakdown.reduce((sum, b) => sum + ((b.cost_USD as number) || 0), 0);

        const result: Record<string, unknown> = {
          period: `${range.start.slice(0, 10)} to ${range.end.slice(0, 10)}`,
          groupBy: args.groupBy,
          summary: {
            totalCalls,
            totalEstimatedTokens,
            totalInputTokens,
            totalOutputTokens,
            estimatedCost_USD: Math.round(totalCost * 1000) / 1000,
            sources: { estimated: estimatedSourceCount, actual: actualSourceCount },
          },
          breakdown: breakdown.slice(0, 50),
        };

        // CSV export
        if (args.exportCsv) {
          mkdirSync(REPORTS_DIR, { recursive: true });
          const csvName = `usage-${range.start.slice(0, 10)}-to-${range.end.slice(0, 10)}.csv`;
          const csvPath = join(REPORTS_DIR, csvName);

          const allRows = queryUsage({ startDate: range.start, endDate: range.end });
          const apiRows = args.includeApiCalls ? readApiUsage(range.start, range.end) : [];

          const csvLines = ["timestamp,tool_name,category,session_id,estimated_tokens,input_tokens,output_tokens,source,model,duration_ms,cost_usd"];

          for (const r of allRows) {
            const tokens = Number(r.estimated_tokens) || 0;
            const cost = estimateCost(Math.ceil(tokens * 0.7), "claude-sonnet-4-6", "input", pricing)
                       + estimateCost(Math.ceil(tokens * 0.3), "claude-sonnet-4-6", "output", pricing);
            csvLines.push(`${r.timestamp},${r.tool_name},${r.tool_category ?? ""},${r.session_id ?? ""},${tokens},,,${r.source},${r.model ?? ""},${r.duration_ms ?? ""},${cost.toFixed(4)}`);
          }

          for (const r of apiRows) {
            const cost = estimateCost(r.input_tokens, r.model ?? "claude-sonnet-4-6", "input", pricing)
                       + estimateCost(r.output_tokens, r.model ?? "claude-sonnet-4-6", "output", pricing);
            const tools = (r.tool_calls ?? []).join(";");
            csvLines.push(`${r.timestamp},${tools},API Call,${r.session_id ?? ""},,${r.input_tokens},${r.output_tokens},actual,${r.model ?? ""},${r.duration_ms ?? ""},${cost.toFixed(4)}`);
          }

          writeFileSync(csvPath, csvLines.join("\n"), "utf-8");
          result.csvPath = csvPath;
        }

        return toolResponse("report_token_usage", result, Date.now() - start, args);
      } catch (error) {
        return toolError("report_token_usage", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 2: Register in register.ts**

Add import at top of `server/src/tools/register.ts` (after line 124):

```typescript
import { registerReportTokenUsageTool } from "./report_token_usage.js";
```

Add to the `toolRegistrations` array (after the `duplicate_system_type` entry at line 250):

```typescript
  { name: "report_token_usage", register: registerReportTokenUsageTool },
```

- [ ] **Step 3: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 4: Regenerate tool-schemas.txt**

Run: `cd C:/Users/luigi.dattilo/Desktop/ClaudeCode/RevitCortex && node server/generate-tool-schemas.mjs`
Expected: `Generated ... with 124 tools`

- [ ] **Step 5: Commit**

```bash
git add server/src/tools/report_token_usage.ts server/src/tools/register.ts server/src/schemas/usage.ts tool-schemas.txt
git commit -m "feat(usage): add report_token_usage MCP tool with CSV export"
```

---

### Task 8: C# UsageTracker

**Files:**
- Create: `src/RevitCortex.Plugin/Tracking/UsageTracker.cs`

- [ ] **Step 1: Create the Tracking directory and UsageTracker class**

```csharp
// src/RevitCortex.Plugin/Tracking/UsageTracker.cs
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.Tracking;

public static class UsageTracker
{
    private static readonly string UsageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".revitcortex");
    private static readonly string UsagePath = Path.Combine(UsageDir, "usage.jsonl");
    private static string? _sessionId;

    public static string SessionId
    {
        get
        {
            if (_sessionId == null)
            {
                var rng = new Random();
                _sessionId = $"{DateTime.Now:yyyyMMdd}-{rng.Next(0x10000):x4}";
            }
            return _sessionId;
        }
    }

    public static void Record(
        string model,
        int inputTokens,
        int outputTokens,
        int thinkingTokens,
        List<string> toolCalls,
        int durationMs)
    {
        try
        {
            Directory.CreateDirectory(UsageDir);

            var entry = new JObject
            {
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["session_id"] = SessionId,
                ["model"] = model,
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens,
                ["thinking_tokens"] = thinkingTokens,
                ["tool_calls"] = new JArray(toolCalls.ToArray()),
                ["source"] = "actual",
                ["duration_ms"] = durationMs,
            };

            File.AppendAllText(UsagePath, entry.ToString(Formatting.None) + "\n");
        }
        catch
        {
            // Silent fail — tracking must never break the chat
        }
    }
}
```

- [ ] **Step 2: Build C# to verify**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Compilazione completata. Avvisi: 0 Errori: 0`

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Plugin/Tracking/UsageTracker.cs
git commit -m "feat(usage): add C# UsageTracker for API call JSONL logging"
```

---

### Task 9: Integrate UsageTracker into CortexChatClient

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/CortexChatClient.cs`

- [ ] **Step 1: Add using directive**

Add at top of `CortexChatClient.cs` (after line 11):

```csharp
using RevitCortex.Plugin.Tracking;
```

- [ ] **Step 2: Track usage in CallClaudeApi**

In the `CallClaudeApi` method, after successfully parsing the response JSON (line 223: `return JObject.Parse(responseText);`), extract usage before returning. Replace lines 221–223:

```csharp
                using var reader = new StreamReader(response.GetResponseStream()!);
                string responseText = await reader.ReadToEndAsync();
                var responseJson = JObject.Parse(responseText);

                // Track token usage
                try
                {
                    var usage = responseJson["usage"];
                    if (usage != null)
                    {
                        int inputTokens = usage["input_tokens"]?.Value<int>() ?? 0;
                        int outputTokens = usage["output_tokens"]?.Value<int>() ?? 0;
                        // thinking tokens are not in standard usage, estimate from content
                        int thinkingTokens = 0;
                        var content = responseJson["content"] as JArray;
                        if (content != null)
                        {
                            foreach (var block in content)
                            {
                                if (block["type"]?.ToString() == "thinking")
                                    thinkingTokens += (block["thinking"]?.ToString() ?? "").Length / 4;
                            }
                        }
                        UsageTracker.Record(_model, inputTokens, outputTokens, thinkingTokens, new List<string>(), 0);
                    }
                }
                catch { /* tracking must not break API flow */ }

                return responseJson;
```

- [ ] **Step 3: Track tool names in ProcessConversation**

In `ProcessConversation` (around line 148), after collecting all tool uses and before adding them to history, record the tool names. Add after the `foreach` loop that processes tool uses (after line 167, before line 169):

```csharp
                // Track tool names for this round's usage
                try
                {
                    var toolNames = toolUses.Select(t => t["name"]?.ToString() ?? "unknown").ToList();
                    // Update the last UsageTracker record with tool names
                    // (The record was already created in CallClaudeApi, but without tool names.
                    //  For simplicity, record a supplementary entry with tool context.)
                }
                catch { }
```

Actually, a cleaner approach: pass tool names back to be included in the usage record. Modify `CallClaudeApi` to accept and store pending tool names, and record them together. But this adds complexity. The simpler approach is to record the API call in `ProcessConversation` instead of `CallClaudeApi`, where we have both the response and the tool context.

**Revised approach — move tracking to ProcessConversation:**

Remove the tracking code from `CallClaudeApi` (revert step 2 to the original). Instead, in `ProcessConversation`, after `var response = await CallClaudeApi();` (line 114), add:

```csharp
            var response = await CallClaudeApi();
            if (response == null) return "No response from API.";

            // Track API token usage
            var roundToolNames = new List<string>();
```

Then after extracting toolUses (after line 134), add:

```csharp
            foreach (var block in content)
            {
                // ... existing code ...
            }

            // Collect tool names for tracking
            foreach (var tu in toolUses)
                roundToolNames.Add(tu["name"]?.ToString() ?? "unknown");
```

Then after adding the assistant message to history (after line 140), track:

```csharp
            _conversationHistory.Add(new JObject { ["role"] = "assistant", ["content"] = content });

            // Record usage
            try
            {
                var usage = response["usage"];
                if (usage != null)
                {
                    int inputTokens = usage["input_tokens"]?.Value<int>() ?? 0;
                    int outputTokens = usage["output_tokens"]?.Value<int>() ?? 0;
                    UsageTracker.Record(_model, inputTokens, outputTokens, 0, roundToolNames, 0);
                }
            }
            catch { }
```

- [ ] **Step 4: Build C# to verify**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Compilazione completata. Avvisi: 0 Errori: 0`

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Plugin/UI/CortexChatClient.cs
git commit -m "feat(usage): extract API token usage in CortexChatClient and log via UsageTracker"
```

---

### Task 10: Initialize Usage DB at Server Startup

**Files:**
- Modify: `server/index.ts` (or wherever the MCP server starts)

- [ ] **Step 1: Find the server entry point**

Read `server/src/index.ts` (or `server/index.ts`) to find where the server initializes. Add a call to `getUsageDatabase()` during startup so the DB is ready before any tool call.

Add import:

```typescript
import { getUsageDatabase } from "./database/usageDb.js";
```

Add initialization call near other startup logic (after database init, before `server.connect`):

```typescript
await getUsageDatabase();
```

- [ ] **Step 2: Build to verify**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 3: Commit**

```bash
git add server/src/index.ts
git commit -m "feat(usage): initialize usage DB at server startup"
```

---

### Task 11: Final Verification

- [ ] **Step 1: Full TS build**

Run: `cd server && npm run build`
Expected: `Build complete: build/index.js`

- [ ] **Step 2: Full C# build**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Compilazione completata. Avvisi: 0 Errori: 0`

- [ ] **Step 3: Regenerate tool-schemas.txt**

Run: `cd C:/Users/luigi.dattilo/Desktop/ClaudeCode/RevitCortex && node server/generate-tool-schemas.mjs`
Expected: `Generated ... with 124 tools`

- [ ] **Step 4: Verify report_token_usage is in tool-schemas.txt**

Run: `grep "report_token_usage" tool-schemas.txt`
Expected: One line containing the tool signature.

- [ ] **Step 5: Final commit if any changes**

```bash
git add -A
git commit -m "feat(usage): token usage tracking - final verification"
```
