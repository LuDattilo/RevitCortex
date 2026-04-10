import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportScheduleInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerExportScheduleTool(server: McpServer): void {
  server.tool("export_schedule", "Export schedule to CSV/TSV or structured JSON", ExportScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_schedule", args);
      });
      return toolResponse("export_schedule", result, Date.now() - start, args);
    } catch (error) {
      return toolError("export_schedule", error, Date.now() - start);
    }
  });
}
