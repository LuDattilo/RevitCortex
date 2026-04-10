import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchExportInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerBatchExportTool(server: McpServer): void {
  server.tool("batch_export", "Export views/sheets to DWG, DXF, DGN, or image formats", BatchExportInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_export", args);
      });
      return toolResponse("batch_export", result, Date.now() - start, args);
    } catch (error) {
      return toolError("batch_export", error, Date.now() - start);
    }
  });
}
