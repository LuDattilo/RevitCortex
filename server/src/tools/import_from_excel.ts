import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ImportFromExcelInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerImportFromExcelTool(server: McpServer): void {
  server.tool("import_from_excel", "Import data from Excel (.xlsx) into Revit element parameters using ElementId matching", ImportFromExcelInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("import_from_excel", args);
      });
      logToolCall({ tool: "import_from_excel", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "import_from_excel", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
