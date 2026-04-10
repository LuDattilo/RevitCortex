import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WipeEmptyTagsInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerWipeEmptyTagsTool(server: McpServer): void {
  server.tool("wipe_empty_tags", "Find and remove empty or orphaned tags", WipeEmptyTagsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("wipe_empty_tags", args);
      });
      return toolResponse("wipe_empty_tags", result, Date.now() - start, args);
    } catch (error) {
      return toolError("wipe_empty_tags", error, Date.now() - start);
    }
  });
}
