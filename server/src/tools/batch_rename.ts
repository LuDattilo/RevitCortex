import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchRenameInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerBatchRenameTool(server: McpServer): void {
  server.tool("batch_rename", "Batch rename views, sheets, levels, grids, or rooms", BatchRenameInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_rename", args);
      });
      logToolCall({ tool: "batch_rename", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "batch_rename", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
