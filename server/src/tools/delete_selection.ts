import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DeleteSelectionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDeleteSelectionTool(server: McpServer): void {
  server.tool("delete_selection", "Delete a saved selection filter", DeleteSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("delete_selection", args);
      });
      return toolResponse("delete_selection", result, Date.now() - start, args);
    } catch (error) {
      return toolError("delete_selection", error, Date.now() - start);
    }
  });
}
