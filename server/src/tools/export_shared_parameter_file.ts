import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportSharedParameterFileInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerExportSharedParameterFileTool(server: McpServer): void {
  server.tool("export_shared_parameter_file", "Export shared parameter file contents", ExportSharedParameterFileInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_shared_parameter_file", args);
      });
      return toolResponse("export_shared_parameter_file", result, Date.now() - start, args);
    } catch (error) {
      return toolError("export_shared_parameter_file", error, Date.now() - start);
    }
  });
}
