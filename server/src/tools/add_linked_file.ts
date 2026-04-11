import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AddLinkedFileInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAddLinkedFileTool(server: McpServer): void {
  server.tool("add_linked_file", "Adds a new Revit linked file from a file path and optionally places an instance at a specified position", AddLinkedFileInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("add_linked_file", args);
      });
      return toolResponse("add_linked_file", result, Date.now() - start, args);
    } catch (error) {
      return toolError("add_linked_file", error, Date.now() - start);
    }
  });
}
