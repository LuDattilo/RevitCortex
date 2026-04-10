import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportFamiliesInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerExportFamiliesTool(server: McpServer): void {
  server.tool("export_families", "Export loaded families as .rfa files to a folder", ExportFamiliesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_families", args);
      });
      logToolCall({ tool: "export_families", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "export_families", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
