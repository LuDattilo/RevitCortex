import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageLinksInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerManageLinksTool(server: McpServer): void {
  server.tool("manage_links", "List, reload, or unload linked Revit/CAD/IFC files", ManageLinksInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_links", args);
      });
      logToolCall({ tool: "manage_links", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "manage_links", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
