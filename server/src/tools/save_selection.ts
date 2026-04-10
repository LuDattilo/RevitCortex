import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SaveSelectionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSaveSelectionTool(server: McpServer): void {
  server.tool("save_selection", "Save element selection as named filter", SaveSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("save_selection", args);
      });
      return toolResponse("save_selection", result, Date.now() - start, args);
    } catch (error) {
      return toolError("save_selection", error, Date.now() - start);
    }
  });
}
