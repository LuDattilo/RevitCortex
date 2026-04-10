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
