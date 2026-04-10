import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DeleteScheduleInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerDeleteScheduleTool(server: McpServer): void {
  server.tool("delete_schedule", "Delete a schedule by ID or name", DeleteScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("delete_schedule", args);
      });
      logToolCall({ tool: "delete_schedule", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "delete_schedule", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
