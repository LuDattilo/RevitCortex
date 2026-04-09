import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ColorElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerColorElementsTool(server: McpServer): void {
  server.tool("color_elements", "Color elements by parameter value using auto, gradient, or custom colors", ColorElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("color_elements", args);
      });
      logToolCall({ tool: "color_elements", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "color_elements", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
