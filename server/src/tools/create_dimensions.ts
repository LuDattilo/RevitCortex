import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateDimensionsInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateDimensionsTool(server: McpServer): void {
  server.tool("create_dimensions", "Create dimension annotations between points or elements", CreateDimensionsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_dimensions", args);
      });
      logToolCall({ tool: "create_dimensions", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_dimensions", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
