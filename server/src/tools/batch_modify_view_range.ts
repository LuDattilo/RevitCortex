import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchModifyViewRangeInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerBatchModifyViewRangeTool(server: McpServer): void {
  server.tool("batch_modify_view_range", "Modify view range offsets for multiple views", BatchModifyViewRangeInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_modify_view_range", args);
      });
      logToolCall({ tool: "batch_modify_view_range", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "batch_modify_view_range", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
