import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageLinksInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerManageLinksTool(server: McpServer): void {
  server.tool("manage_links", "List, reload, or unload linked Revit/CAD/IFC files", ManageLinksInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_links", args);
      });
      return toolResponse("manage_links", result, Date.now() - start, args);
    } catch (error) {
      return toolError("manage_links", error, Date.now() - start);
    }
  });
}
