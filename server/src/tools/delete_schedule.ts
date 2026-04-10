import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DeleteScheduleInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDeleteScheduleTool(server: McpServer): void {
  server.tool("delete_schedule", "Delete a schedule by ID or name", DeleteScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("delete_schedule", args);
      });
      return toolResponse("delete_schedule", result, Date.now() - start, args);
    } catch (error) {
      return toolError("delete_schedule", error, Date.now() - start);
    }
  });
}
