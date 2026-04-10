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
