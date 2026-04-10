import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportSharedParameterFileInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerExportSharedParameterFileTool(server: McpServer): void {
  server.tool("export_shared_parameter_file", "Export shared parameter file contents", ExportSharedParameterFileInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_shared_parameter_file", args);
      });
      logToolCall({ tool: "export_shared_parameter_file", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "export_shared_parameter_file", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
