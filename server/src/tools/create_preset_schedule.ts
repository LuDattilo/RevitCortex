import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreatePresetScheduleInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreatePresetScheduleTool(server: McpServer): void {
  server.tool("create_preset_schedule", "Create preset schedules (door, window, room finish, material takeoff, etc.)", CreatePresetScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_preset_schedule", args);
      });
      logToolCall({ tool: "create_preset_schedule", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_preset_schedule", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
