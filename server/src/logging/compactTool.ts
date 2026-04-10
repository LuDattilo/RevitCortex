import { compactResponse } from "./responseCompactor.js";
import { logTokenUsage } from "./tokenLogger.js";
import { logToolCall } from "./logger.js";

/**
 * Standard tool response wrapper with compaction, token logging, and stderr logging.
 * Replaces raw JSON.stringify + logToolCall in every tool file.
 */
export function toolResponse(
  toolName: string,
  result: unknown,
  durationMs: number,
  args?: Record<string, unknown>
): { content: Array<{ type: "text"; text: string }> } {
  const compacted = compactResponse(result, {
    compact: (args?.compact as boolean) ?? false,
    stripNulls: true,
    maxArrayItems: 100,
  });
  const text = JSON.stringify(compacted, null, 2);
  logTokenUsage(toolName, text, durationMs, false);
  logToolCall({ tool: toolName, success: true, durationMs });
  return { content: [{ type: "text" as const, text }] };
}

/**
 * Standard error response wrapper with token logging and stderr logging.
 */
export function toolError(
  toolName: string,
  error: unknown,
  durationMs: number
): { content: Array<{ type: "text"; text: string }>; isError: true } {
  const message = `Error: ${error instanceof Error ? error.message : String(error)}`;
  logTokenUsage(toolName, message, durationMs, true);
  logToolCall({ tool: toolName, success: false, durationMs });
  return { content: [{ type: "text" as const, text: message }], isError: true };
}
