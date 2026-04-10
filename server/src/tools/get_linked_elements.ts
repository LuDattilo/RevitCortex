import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetLinkedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetLinkedElementsTool(server: McpServer): void {
  server.tool(
    "get_linked_elements",
    "Query elements from linked Revit models by category",
    GetLinkedElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_linked_elements", args);
        });
        return toolResponse("get_linked_elements", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_linked_elements", error, Date.now() - start);
      }
    }
  );
}
