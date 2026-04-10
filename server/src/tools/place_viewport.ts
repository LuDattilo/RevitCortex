import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { PlaceViewportInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerPlaceViewportTool(server: McpServer): void {
  server.tool("place_viewport", "Place a view on a sheet at the specified position", PlaceViewportInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("place_viewport", args);
      });
      logToolCall({ tool: "place_viewport", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "place_viewport", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
