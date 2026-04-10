import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateViewInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDuplicateViewTool(server: McpServer): void {
  server.tool("duplicate_view", "Duplicate views with configurable options", DuplicateViewInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_view", args);
      });
      return toolResponse("duplicate_view", result, Date.now() - start, args);
    } catch (error) {
      return toolError("duplicate_view", error, Date.now() - start);
    }
  });
}
