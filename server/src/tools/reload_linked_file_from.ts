import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ReloadLinkedFileFromInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerReloadLinkedFileFromTool(server: McpServer): void {
  server.tool("reload_linked_file_from", "Reloads a linked Revit file from a different file path (repath)", ReloadLinkedFileFromInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("reload_linked_file_from", args);
      });
      return toolResponse("reload_linked_file_from", result, Date.now() - start, args);
    } catch (error) {
      return toolError("reload_linked_file_from", error, Date.now() - start);
    }
  });
}
