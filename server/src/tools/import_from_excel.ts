import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ImportFromExcelInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerImportFromExcelTool(server: McpServer): void {
  server.tool("import_from_excel", "Import data from Excel (.xlsx) into Revit element parameters using ElementId matching", ImportFromExcelInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("import_from_excel", args);
      });
      return toolResponse("import_from_excel", result, Date.now() - start, args);
    } catch (error) {
      return toolError("import_from_excel", error, Date.now() - start);
    }
  });
}
