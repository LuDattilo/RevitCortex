import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateSheetWithViewsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDuplicateSheetWithViewsTool(server: McpServer): void {
  server.tool("duplicate_sheet_with_views", "Duplicate a sheet with configurable view duplication options", DuplicateSheetWithViewsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_sheet_with_views", args);
      });
      return toolResponse("duplicate_sheet_with_views", result, Date.now() - start, args);
    } catch (error) {
      return toolError("duplicate_sheet_with_views", error, Date.now() - start);
    }
  });
}
