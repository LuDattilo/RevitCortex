import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateColorLegendInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateColorLegendTool(server: McpServer): void {
  server.tool("create_color_legend", "Color elements by parameter value with auto/gradient/custom colors and optional legend view", CreateColorLegendInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_color_legend", args);
      });
      return toolResponse("create_color_legend", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_color_legend", error, Date.now() - start);
    }
  });
}
