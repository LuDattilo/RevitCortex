import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetElementsInSpatialVolumeInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetElementsInSpatialVolumeTool(server: McpServer): void {
  server.tool(
    "get_elements_in_spatial_volume",
    "Find elements within rooms, areas, or custom bounding boxes",
    GetElementsInSpatialVolumeInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_elements_in_spatial_volume", args);
        });
        return toolResponse("get_elements_in_spatial_volume", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_elements_in_spatial_volume", error, Date.now() - start);
      }
    }
  );
}
