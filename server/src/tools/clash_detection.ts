import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ClashDetectionInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerClashDetectionTool(server: McpServer): void {
  server.tool("clash_detection", "Detect geometric intersections between element sets", ClashDetectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("clash_detection", args);
      });
      logToolCall({ tool: "clash_detection", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "clash_detection", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
