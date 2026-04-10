import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreatePlaceholderSheetsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreatePlaceholderSheetsTool(server: McpServer): void {
  server.tool("create_placeholder_sheets", "Create, list, convert, or delete placeholder sheets", CreatePlaceholderSheetsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_placeholder_sheets", args);
      });
      logToolCall({ tool: "create_placeholder_sheets", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_placeholder_sheets", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
