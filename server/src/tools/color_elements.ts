import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ColorElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerColorElementsTool(server: McpServer): void {
  server.tool("color_elements", "Color elements by parameter value using auto, gradient, or custom colors", ColorElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("color_elements", args);
      });
      return toolResponse("color_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("color_elements", error, Date.now() - start);
    }
  });
}
