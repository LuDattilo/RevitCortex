import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportFamiliesInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerExportFamiliesTool(server: McpServer): void {
  server.tool("export_families", "Export loaded families as .rfa files to a folder", ExportFamiliesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_families", args);
      });
      return toolResponse("export_families", result, Date.now() - start, args);
    } catch (error) {
      return toolError("export_families", error, Date.now() - start);
    }
  });
}
