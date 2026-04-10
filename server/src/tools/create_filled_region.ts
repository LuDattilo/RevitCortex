import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateFilledRegionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateFilledRegionTool(server: McpServer): void {
  server.tool("create_filled_region", "Create a filled region from boundary points", CreateFilledRegionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_filled_region", args);
      });
      logToolCall({ tool: "create_filled_region", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_filled_region", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
