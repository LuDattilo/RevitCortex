import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ChangeElementTypeInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerChangeElementTypeTool(server: McpServer): void {
  server.tool(
    "change_element_type",
    "Change family type of elements by type ID or name",
    ChangeElementTypeInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("change_element_type", args);
        });
        return toolResponse("change_element_type", result, Date.now() - start, args);
      } catch (error) {
        return toolError("change_element_type", error, Date.now() - start);
      }
    }
  );
}
