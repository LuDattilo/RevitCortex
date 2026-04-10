import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateColorLegendInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateColorLegendTool(server: McpServer): void {
  server.tool("create_color_legend", "Color elements by parameter value with auto/gradient/custom colors and optional legend view", CreateColorLegendInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_color_legend", args);
      });
      logToolCall({ tool: "create_color_legend", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_color_legend", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
