import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { RenameViewsInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerRenameViewsTool(server: McpServer): void {
  server.tool("rename_views", "Rename views with find/replace, prefix, or suffix", RenameViewsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("rename_views", args);
      });
      return toolResponse("rename_views", result, Date.now() - start, args);
    } catch (error) {
      return toolError("rename_views", error, Date.now() - start);
    }
  });
}
