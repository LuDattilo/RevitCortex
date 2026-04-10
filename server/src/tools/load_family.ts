import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { LoadFamilyInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerLoadFamilyTool(server: McpServer): void {
  server.tool("load_family", "Load .rfa family, list loaded families, or duplicate a type", LoadFamilyInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("load_family", args);
      });
      return toolResponse("load_family", result, Date.now() - start, args);
    } catch (error) {
      return toolError("load_family", error, Date.now() - start);
    }
  });
}
