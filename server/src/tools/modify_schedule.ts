import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ModifyScheduleInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerModifyScheduleTool(server: McpServer): void {
  server.tool("modify_schedule", "Modify schedule fields, sorting, or rename", ModifyScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("modify_schedule", args);
      });
      return toolResponse("modify_schedule", result, Date.now() - start, args);
    } catch (error) {
      return toolError("modify_schedule", error, Date.now() - start);
    }
  });
}
