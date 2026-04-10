import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportElementsDataInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerExportElementsDataTool(server: McpServer): void {
  server.tool("export_elements_data", "Export element data by category as JSON or CSV with optional filtering", ExportElementsDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_elements_data", args);
      });
      return toolResponse("export_elements_data", result, Date.now() - start, args);
    } catch (error) {
      return toolError("export_elements_data", error, Date.now() - start);
    }
  });
}
