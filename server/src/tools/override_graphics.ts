import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { OverrideGraphicsInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerOverrideGraphicsTool(server: McpServer): void {
  server.tool("override_graphics", "Set or reset graphic overrides for elements in a view", OverrideGraphicsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("override_graphics", args);
      });
      return toolResponse("override_graphics", result, Date.now() - start, args);
    } catch (error) {
      return toolError("override_graphics", error, Date.now() - start);
    }
  });
}
