import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateSheetWithViewsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerDuplicateSheetWithViewsTool(server: McpServer): void {
  server.tool("duplicate_sheet_with_views", "Duplicate a sheet with configurable view duplication options", DuplicateSheetWithViewsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_sheet_with_views", args);
      });
      logToolCall({ tool: "duplicate_sheet_with_views", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "duplicate_sheet_with_views", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
