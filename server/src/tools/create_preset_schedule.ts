import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreatePresetScheduleInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreatePresetScheduleTool(server: McpServer): void {
  server.tool("create_preset_schedule", "Create preset schedules (door, window, room finish, material takeoff, etc.)", CreatePresetScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_preset_schedule", args);
      });
      return toolResponse("create_preset_schedule", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_preset_schedule", error, Date.now() - start);
    }
  });
}
