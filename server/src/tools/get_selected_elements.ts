import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetSelectedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetSelectedElementsTool(server: McpServer): void {
  server.tool(
    "get_selected_elements",
    "Get info about currently selected elements in Revit",
    GetSelectedElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_selected_elements", args);
        });
        return toolResponse("get_selected_elements", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_selected_elements", error, Date.now() - start);
      }
    }
  );
}
