import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { RenameFamiliesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerRenameFamiliesTool(server: McpServer): void {
  server.tool("rename_families", "Rename loaded families with find/replace, prefix, or suffix", RenameFamiliesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("rename_families", args);
      });
      return toolResponse("rename_families", result, Date.now() - start, args);
    } catch (error) {
      return toolError("rename_families", error, Date.now() - start);
    }
  });
}
