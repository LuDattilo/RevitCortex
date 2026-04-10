import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { LoadSelectionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerLoadSelectionTool(server: McpServer): void {
  server.tool("load_selection", "List or load saved selections", LoadSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("load_selection", args);
      });
      return toolResponse("load_selection", result, Date.now() - start, args);
    } catch (error) {
      return toolError("load_selection", error, Date.now() - start);
    }
  });
}
