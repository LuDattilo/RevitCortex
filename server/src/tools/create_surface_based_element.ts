import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateSurfaceBasedElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateSurfaceBasedElementTool(server: McpServer): void {
  server.tool("create_surface_based_element", "Create floors, ceilings, roofs from boundary polygons", CreateSurfaceBasedElementInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_surface_based_element", args);
      });
      logToolCall({ tool: "create_surface_based_element", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_surface_based_element", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
