import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportToExcelInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerExportToExcelTool(server: McpServer): void {
  server.tool("export_to_excel", "Export elements by category to Excel (.xlsx) with color-coded instance/type parameter columns", ExportToExcelInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_to_excel", args);
      });
      return toolResponse("export_to_excel", result, Date.now() - start, args);
    } catch (error) {
      return toolError("export_to_excel", error, Date.now() - start);
    }
  });
}
