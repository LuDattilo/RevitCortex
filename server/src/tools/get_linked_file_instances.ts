import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetLinkedFileInstancesInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetLinkedFileInstancesTool(server: McpServer): void {
  server.tool("get_linked_file_instances", "Lists all linked Revit files grouped by type, with instance transforms, load status, and file paths", GetLinkedFileInstancesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("get_linked_file_instances", args);
      });
      return toolResponse("get_linked_file_instances", result, Date.now() - start, args);
    } catch (error) {
      return toolError("get_linked_file_instances", error, Date.now() - start);
    }
  });
}
