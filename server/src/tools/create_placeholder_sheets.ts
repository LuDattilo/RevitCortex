import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreatePlaceholderSheetsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreatePlaceholderSheetsTool(server: McpServer): void {
  server.tool("create_placeholder_sheets", "Create, list, convert, or delete placeholder sheets", CreatePlaceholderSheetsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_placeholder_sheets", args);
      });
      return toolResponse("create_placeholder_sheets", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_placeholder_sheets", error, Date.now() - start);
    }
  });
}
