import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CadLinkCleanupInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCadLinkCleanupTool(server: McpServer): void {
  server.tool("cad_link_cleanup", "Analyze and clean up imported/linked CAD files", CadLinkCleanupInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("cad_link_cleanup", args);
      });
      return toolResponse("cad_link_cleanup", result, Date.now() - start, args);
    } catch (error) {
      return toolError("cad_link_cleanup", error, Date.now() - start);
    }
  });
}
