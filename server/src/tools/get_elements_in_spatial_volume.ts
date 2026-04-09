import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetElementsInSpatialVolumeInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

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
        logToolCall({ tool: "get_elements_in_spatial_volume", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_elements_in_spatial_volume", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
