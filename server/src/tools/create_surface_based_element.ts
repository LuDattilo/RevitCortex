import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateSurfaceBasedElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateSurfaceBasedElementTool(server: McpServer): void {
  server.tool("create_surface_based_element", "Create floors, ceilings, roofs from boundary polygons", CreateSurfaceBasedElementInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_surface_based_element", args);
      });
      return toolResponse("create_surface_based_element", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_surface_based_element", error, Date.now() - start);
    }
  });
}
