import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchCreateSheetsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerBatchCreateSheetsTool(server: McpServer): void {
  server.tool("batch_create_sheets", "Create multiple sheets with title blocks and optional view placement", BatchCreateSheetsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_create_sheets", args);
      });
      logToolCall({ tool: "batch_create_sheets", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "batch_create_sheets", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
