import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateScheduleInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateScheduleTool(server: McpServer): void {
  server.tool("create_schedule", "Create a schedule view with fields and filters", CreateScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_schedule", args);
      });
      return toolResponse("create_schedule", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_schedule", error, Date.now() - start);
    }
  });
}
