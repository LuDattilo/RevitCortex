import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchCreateSheetsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerBatchCreateSheetsTool(server: McpServer): void {
  server.tool("batch_create_sheets", "Create multiple sheets with title blocks and optional view placement", BatchCreateSheetsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_create_sheets", args);
      });
      return toolResponse("batch_create_sheets", result, Date.now() - start, args);
    } catch (error) {
      return toolError("batch_create_sheets", error, Date.now() - start);
    }
  });
}
