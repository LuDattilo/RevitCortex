import initSqlJs, { Database as SqlJsDatabase } from "sql.js";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import { homedir } from "os";
import { mkdirSync, readFileSync, writeFileSync, renameSync } from "fs";

const __bundledir = dirname(fileURLToPath(import.meta.url));

const DB_DIR = join(homedir(), ".revitcortex");
mkdirSync(DB_DIR, { recursive: true });
const DB_PATH = join(DB_DIR, "revitcortex-data.db");

let db: SqlJsDatabase | null = null;
let savePending = false;

// H37: write to a temp file then atomically rename over the target. A SIGKILL or
// power loss mid-write would otherwise leave a truncated DB file, and getDatabase()'s
// catch{} silently replaces it with an empty database — destroying all stored data.
// renameSync on the same filesystem is atomic on both POSIX and Windows (NTFS).
function atomicWriteDb() {
  if (!db) return;
  const tmp = DB_PATH + ".tmp";
  writeFileSync(tmp, Buffer.from(db.export()));
  renameSync(tmp, DB_PATH);
}

function scheduleSave() {
  if (savePending) return;
  savePending = true;
  setImmediate(() => {
    savePending = false;
    atomicWriteDb();
  });
}

function flushDatabase() {
  savePending = false;
  atomicWriteDb();
}

export async function getDatabase(): Promise<SqlJsDatabase> {
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

  db.run("PRAGMA foreign_keys = ON");
  initializeSchema(db);
  return db;
}

function initializeSchema(database: SqlJsDatabase) {
  database.run(`
    CREATE TABLE IF NOT EXISTS projects (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_name TEXT NOT NULL,
      project_path TEXT,
      project_number TEXT,
      project_address TEXT,
      client_name TEXT,
      project_status TEXT,
      author TEXT,
      timestamp INTEGER NOT NULL,
      last_updated INTEGER NOT NULL,
      metadata TEXT
    )
  `);

  database.run(`
    CREATE TABLE IF NOT EXISTS rooms (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_id INTEGER NOT NULL,
      room_id TEXT NOT NULL,
      room_name TEXT,
      room_number TEXT,
      department TEXT,
      level TEXT,
      area REAL,
      perimeter REAL,
      occupancy TEXT,
      comments TEXT,
      timestamp INTEGER NOT NULL,
      metadata TEXT,
      FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
      UNIQUE(project_id, room_id)
    )
  `);

  database.run("CREATE INDEX IF NOT EXISTS idx_projects_name ON projects(project_name)");
  database.run("CREATE INDEX IF NOT EXISTS idx_rooms_project_id ON rooms(project_id)");
  database.run("CREATE INDEX IF NOT EXISTS idx_rooms_room_number ON rooms(room_number)");
}

function getDb(): SqlJsDatabase {
  if (!db) throw new Error("Database not initialized. Call getDatabase() first.");
  return db;
}

export function dbRun(sql: string, params?: any[]): void {
  getDb().run(sql, params);
  scheduleSave();
}

export function dbGet(sql: string, params?: any[]): any {
  const stmt = getDb().prepare(sql);
  if (params) stmt.bind(params);
  const result = stmt.step() ? stmt.getAsObject() : undefined;
  stmt.free();
  return result;
}

export function dbAll(sql: string, params?: any[]): any[] {
  const results: any[] = [];
  const stmt = getDb().prepare(sql);
  if (params) stmt.bind(params);
  while (stmt.step()) results.push(stmt.getAsObject());
  stmt.free();
  return results;
}

export function dbLastInsertRowid(): number {
  const result = dbGet("SELECT last_insert_rowid() as id");
  return result?.id as number;
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
