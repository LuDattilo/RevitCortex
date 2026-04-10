import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateSheetWithContentInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerDuplicateSheetWithContentTool(server: McpServer): void {
  server.tool("duplicate_sheet_with_content", "Duplicate a sheet including annotations and detail items", DuplicateSheetWithContentInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_sheet_with_content", args);
      });
      return toolResponse("duplicate_sheet_with_content", result, Date.now() - start, args);
    } catch (error) {
      return toolError("duplicate_sheet_with_content", error, Date.now() - start);
    }
  });
}
