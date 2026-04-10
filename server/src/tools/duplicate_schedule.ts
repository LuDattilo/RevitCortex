import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateScheduleInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDuplicateScheduleTool(server: McpServer): void {
  server.tool("duplicate_schedule", "Duplicate a schedule with a new name", DuplicateScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_schedule", args);
      });
      return toolResponse("duplicate_schedule", result, Date.now() - start, args);
    } catch (error) {
      return toolError("duplicate_schedule", error, Date.now() - start);
    }
  });
}
