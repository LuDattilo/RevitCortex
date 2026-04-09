import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetCurrentViewElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetCurrentViewElementsTool(server: McpServer): void {
  server.tool(
    "get_current_view_elements",
    "Get elements from active view with category/field filtering",
    GetCurrentViewElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_current_view_elements", args);
        });
        logToolCall({ tool: "get_current_view_elements", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_current_view_elements", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
