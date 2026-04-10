import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DeleteElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDeleteElementTool(server: McpServer): void {
  server.tool(
    "delete_element",
    "Delete elements by ID. Defaults to dryRun=true (preview). Set dryRun=false to delete.",
    DeleteElementInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("delete_element", args);
        });
        return toolResponse("delete_element", result, Date.now() - start, args);
      } catch (error) {
        return toolError("delete_element", error, Date.now() - start);
      }
    }
  );
}
