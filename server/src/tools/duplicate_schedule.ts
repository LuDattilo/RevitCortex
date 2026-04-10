import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateScheduleInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerDuplicateScheduleTool(server: McpServer): void {
  server.tool("duplicate_schedule", "Duplicate a schedule with a new name", DuplicateScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_schedule", args);
      });
      logToolCall({ tool: "duplicate_schedule", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "duplicate_schedule", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
