import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchExportInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerBatchExportTool(server: McpServer): void {
  server.tool("batch_export", "Export views/sheets to DWG, DXF, DGN, or image formats", BatchExportInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_export", args);
      });
      logToolCall({ tool: "batch_export", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "batch_export", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
