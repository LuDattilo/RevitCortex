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
