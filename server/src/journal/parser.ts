import { readFileSync } from "fs";

// ── Line types ──────────────────────────────────────────────────────────────

export type JournalLineType =
  | "timestamp"
  | "command"
  | "ribbon_event"
  | "data"
  | "directive"
  | "dialog"
  | "memory"
  | "gdi"
  | "api_message"
  | "addin_manifest"
  | "basic_file_info"
  | "document_close"
  | "external_event"
  | "performance"
  | "comment"
  | "other";

export interface JournalLine {
  type: JournalLineType;
  lineNum: number;
  raw: string;
  data: Record<string, unknown>;
}

export interface JournalHeader {
  revitVersion: string;
  build: string;
  branch: string;
  startTime: Date | null;
}

export interface ParsedJournal {
  filePath: string;
  header: JournalHeader;
  lines: JournalLine[];
  malformedCount: number;
}

// ── Regex patterns ──────────────────────────────────────────────────────────

const TIMESTAMP_RE =
  /^'([CEH])\s+(\d{1,2}-\w{3}-\d{4}\s+\d{2}:\d{2}:\d{2}\.\d{3});\s*(.*)/;
const COMMAND_RE = /^Jrn\.Command\s+"([^"]*)"\s*,\s*"([^"]*)"/;
const RIBBON_EVENT_RE = /^Jrn\.RibbonEvent\s+"([^"]*)"/;
const DATA_RE = /^Jrn\.Data\s+"([^"]*)"(?:\s*,\s*"([^"]*)")?/;
const DIRECTIVE_RE = /^Jrn\.Directive\s+"([^"]*)"(?:\s*,\s*"([^"]*)")?/;
const DIALOG_RE =
  /^Jrn\.(PushButton|CheckBox|Edit|ComboBox|RadioButton)\s+"([^"]*)"(?:\s*,\s*"([^"]*)")?/;
const MEMORY_RE =
  /Delta VM:\s*Avail\s*([+-]?\d+)\s*->\s*(\d+)\s*MB,\s*Used\s*([+-]?\d+)\s*->\s*(\d+)\s*MB,\s*Peak\s*([+-]?\d+)\s*->\s*(\d+)\s*MB;\s*RAM:\s*Avail\s*([+-]?\d+)\s*->\s*(\d+)\s*MB,\s*Used\s*([+-]?\d+)\s*->\s*(\d+)\s*MB,\s*Peak\s*([+-]?\d+)\s*->\s*(\d+)\s*MB/;
const GDI_RE =
  /GUI Resource Usage GDI:\s*Avail\s*(\d+),\s*Used\s*(\d+),\s*User:\s*Used\s*(\d+)/;
const API_MSG_RE = /API_(SUCCESS|ERROR)\s*\{\s*(.*?)\s*\}/;
const ADDIN_MANIFEST_RE = /\[Jrn\.AddInManifest\]/;
const BASIC_FILE_INFO_RE = /\[Jrn\.BasicFileInfo\]/;
const DOCUMENT_CLOSE_RE = /\[Jrn\.CloseDocumentFile\]/;
const EXTERNAL_EVENT_RE = /\[Jrn\.ExternalEventExecution\]/;
const PERFORMANCE_RE = /^\s*([\d.]+)\s+(\d+):<<(.+)/;
const BUILD_RE = /^'\s*Build:\s*(.+)/;
const BRANCH_RE = /^'\s*Branch:\s*(.+)/;
const RELEASE_RE = /^'\s*Release:\s*(.+)/;

// ── Timestamp parser ────────────────────────────────────────────────────────

const MONTHS: Record<string, number> = {
  Jan: 0, Feb: 1, Mar: 2, Apr: 3, May: 4, Jun: 5,
  Jul: 6, Aug: 7, Sep: 8, Oct: 9, Nov: 10, Dec: 11,
};

function parseJournalTimestamp(s: string): Date | null {
  // "10-Apr-2026 11:53:41.709"
  const m = s.match(/(\d{1,2})-(\w{3})-(\d{4})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})/);
  if (!m) return null;
  const month = MONTHS[m[2]];
  if (month === undefined) return null;
  return new Date(+m[3], month, +m[1], +m[4], +m[5], +m[6], +m[7]);
}

// ── Main parser ─────────────────────────────────────────────────────────────

const MAX_LINES = 100_000;

export function parseJournal(filePath: string): ParsedJournal {
  const content = readFileSync(filePath, "utf-8");
  const rawLines = content.split(/\r?\n/);

  const header: JournalHeader = {
    revitVersion: "",
    build: "",
    branch: "",
    startTime: null,
  };
  const lines: JournalLine[] = [];
  let malformedCount = 0;

  // Join VBScript continuation lines (trailing _)
  const joined: string[] = [];
  let buffer = "";
  for (const raw of rawLines) {
    if (buffer) {
      buffer += " " + raw.trimStart();
    } else {
      buffer = raw;
    }
    // Continuation: line ends with _ (possibly preceded by space/tab)
    if (/\s_$|^[^']*_$/.test(buffer.trimEnd())) {
      buffer = buffer.trimEnd().slice(0, -1).trimEnd();
    } else {
      joined.push(buffer);
      buffer = "";
    }
  }
  if (buffer) joined.push(buffer);

  let lineCount = 0;
  for (let i = 0; i < joined.length && lineCount < MAX_LINES; i++) {
    const raw = joined[i];
    lineCount++;

    // Parse header fields
    const buildMatch = raw.match(BUILD_RE);
    if (buildMatch) { header.build = buildMatch[1].trim(); }
    const branchMatch = raw.match(BRANCH_RE);
    if (branchMatch) { header.branch = branchMatch[1].trim(); }
    const releaseMatch = raw.match(RELEASE_RE);
    if (releaseMatch) { header.revitVersion = releaseMatch[1].trim(); }

    const line = classifyLine(raw, i + 1);
    if (line) {
      lines.push(line);
      // Capture first timestamp as start time
      if (!header.startTime && line.type === "timestamp" && line.data.datetime) {
        header.startTime = line.data.datetime as Date;
      }
    } else {
      malformedCount++;
    }
  }

  return { filePath, header, lines, malformedCount };
}

function classifyLine(raw: string, lineNum: number): JournalLine | null {
  const trimmed = raw.trimStart();

  // Timestamp lines: 'C, 'E, 'H
  const tsMatch = trimmed.match(TIMESTAMP_RE);
  if (tsMatch) {
    return {
      type: "timestamp",
      lineNum,
      raw,
      data: {
        tsType: tsMatch[1],
        datetime: parseJournalTimestamp(tsMatch[2]),
        message: tsMatch[3].trim(),
      },
    };
  }

  // Jrn.Command
  const cmdMatch = trimmed.match(COMMAND_RE);
  if (cmdMatch) {
    // Parse "command description, ID_NAME" from the second group
    const parts = cmdMatch[2].split(",").map((s) => s.trim());
    return {
      type: "command",
      lineNum,
      raw,
      data: {
        scope: cmdMatch[1],
        description: parts[0] || "",
        commandId: parts[1] || "",
      },
    };
  }

  // Jrn.RibbonEvent
  const ribbonMatch = trimmed.match(RIBBON_EVENT_RE);
  if (ribbonMatch) {
    return {
      type: "ribbon_event",
      lineNum,
      raw,
      data: { event: ribbonMatch[1] },
    };
  }

  // Jrn.Data
  const dataMatch = trimmed.match(DATA_RE);
  if (dataMatch) {
    return {
      type: "data",
      lineNum,
      raw,
      data: { key: dataMatch[1], value: dataMatch[2] || "" },
    };
  }

  // Jrn.Directive
  const dirMatch = trimmed.match(DIRECTIVE_RE);
  if (dirMatch) {
    return {
      type: "directive",
      lineNum,
      raw,
      data: { name: dirMatch[1], value: dirMatch[2] || "" },
    };
  }

  // Dialog interactions
  const dlgMatch = trimmed.match(DIALOG_RE);
  if (dlgMatch) {
    return {
      type: "dialog",
      lineNum,
      raw,
      data: {
        action: dlgMatch[1],
        dialog: dlgMatch[2],
        control: dlgMatch[3] || "",
      },
    };
  }

  // Memory checkpoint
  const memMatch = trimmed.match(MEMORY_RE);
  if (memMatch) {
    return {
      type: "memory",
      lineNum,
      raw,
      data: {
        vmAvail: +memMatch[2],
        vmUsed: +memMatch[4],
        vmPeak: +memMatch[6],
        ramAvail: +memMatch[8],
        ramUsed: +memMatch[10],
        ramPeak: +memMatch[12],
      },
    };
  }

  // GDI resources
  const gdiMatch = trimmed.match(GDI_RE);
  if (gdiMatch) {
    return {
      type: "gdi",
      lineNum,
      raw,
      data: {
        gdiAvail: +gdiMatch[1],
        gdiUsed: +gdiMatch[2],
        userUsed: +gdiMatch[3],
      },
    };
  }

  // API messages
  const apiMatch = trimmed.match(API_MSG_RE);
  if (apiMatch) {
    return {
      type: "api_message",
      lineNum,
      raw,
      data: { level: apiMatch[1], message: apiMatch[2] },
    };
  }

  // Structured bracket annotations
  if (ADDIN_MANIFEST_RE.test(trimmed)) {
    return {
      type: "addin_manifest",
      lineNum,
      raw,
      data: parseKeyValueBracket(trimmed),
    };
  }
  if (BASIC_FILE_INFO_RE.test(trimmed)) {
    return {
      type: "basic_file_info",
      lineNum,
      raw,
      data: parseKeyValueBracket(trimmed),
    };
  }
  if (DOCUMENT_CLOSE_RE.test(trimmed)) {
    return { type: "document_close", lineNum, raw, data: {} };
  }
  if (EXTERNAL_EVENT_RE.test(trimmed)) {
    return {
      type: "external_event",
      lineNum,
      raw,
      data: parseKeyValueBracket(trimmed),
    };
  }

  // Performance timing
  const perfMatch = trimmed.match(PERFORMANCE_RE);
  if (perfMatch) {
    return {
      type: "performance",
      lineNum,
      raw,
      data: {
        elapsedSec: +perfMatch[1],
        thread: +perfMatch[2],
        operation: perfMatch[3].trim(),
      },
    };
  }

  // Generic comment
  if (trimmed.startsWith("'")) {
    return { type: "comment", lineNum, raw, data: { text: trimmed.slice(1).trim() } };
  }

  // Other (Jrn.MouseMove, Jrn.LButtonDown, Jrn.Wheel, Dim, Set, etc.)
  if (trimmed.length === 0) return null;
  return { type: "other", lineNum, raw, data: {} };
}

/**
 * Parse key=value pairs from bracket-annotated lines like:
 * [Jrn.AddInManifest] Name="Foo" GUID="..." Class="..." ...
 */
function parseKeyValueBracket(raw: string): Record<string, string> {
  const result: Record<string, string> = {};
  // Remove the bracket tag
  const afterBracket = raw.replace(/\[Jrn\.\w+\]\s*/, "");
  // Match Key="Value" or Key=Value patterns
  const pairs = afterBracket.matchAll(/(\w+)\s*=\s*"([^"]*)"/g);
  for (const m of pairs) {
    result[m[1]] = m[2];
  }
  return result;
}
