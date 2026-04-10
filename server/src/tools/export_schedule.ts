import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportScheduleInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerExportScheduleTool(server: McpServer): void {
  server.tool("export_schedule", "Export schedule to CSV/TSV or structured JSON", ExportScheduleInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_schedule", args);
      });
      logToolCall({ tool: "export_schedule", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "export_schedule", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
