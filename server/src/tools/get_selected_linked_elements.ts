import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetSelectedLinkedElementsInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetSelectedLinkedElementsTool(server: McpServer): void {
  server.tool("get_selected_linked_elements", "Returns info about currently selected link instances: load status, path, and element counts by category", GetSelectedLinkedElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("get_selected_linked_elements", args);
      });
      return toolResponse("get_selected_linked_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("get_selected_linked_elements", error, Date.now() - start);
    }
  });
}
