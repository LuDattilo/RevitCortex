import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportElementsDataInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerExportElementsDataTool(server: McpServer): void {
  server.tool("export_elements_data", "Export element data by category as JSON or CSV with optional filtering", ExportElementsDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_elements_data", args);
      });
      logToolCall({ tool: "export_elements_data", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "export_elements_data", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
