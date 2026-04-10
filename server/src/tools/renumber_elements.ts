import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { RenumberElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerRenumberElementsTool(server: McpServer): void {
  server.tool("renumber_elements", "Renumber rooms/doors/windows by location or name. dryRun=true by default.", RenumberElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("renumber_elements", args);
      });
      return toolResponse("renumber_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("renumber_elements", error, Date.now() - start);
    }
  });
}
