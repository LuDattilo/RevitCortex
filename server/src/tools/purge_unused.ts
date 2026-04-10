import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { PurgeUnusedInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerPurgeUnusedTool(server: McpServer): void {
  server.tool("purge_unused", "Find and remove unused families, types, and materials", PurgeUnusedInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("purge_unused", args);
      });
      return toolResponse("purge_unused", result, Date.now() - start, args);
    } catch (error) {
      return toolError("purge_unused", error, Date.now() - start);
    }
  });
}
