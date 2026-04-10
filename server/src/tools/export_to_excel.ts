import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportToExcelInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerExportToExcelTool(server: McpServer): void {
  server.tool("export_to_excel", "Export elements by category to Excel (.xlsx) with color-coded instance/type parameter columns", ExportToExcelInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_to_excel", args);
      });
      logToolCall({ tool: "export_to_excel", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "export_to_excel", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
