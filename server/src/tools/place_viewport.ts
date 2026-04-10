import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { PlaceViewportInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerPlaceViewportTool(server: McpServer): void {
  server.tool("place_viewport", "Place a view on a sheet at the specified position", PlaceViewportInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("place_viewport", args);
      });
      return toolResponse("place_viewport", result, Date.now() - start, args);
    } catch (error) {
      return toolError("place_viewport", error, Date.now() - start);
    }
  });
}
