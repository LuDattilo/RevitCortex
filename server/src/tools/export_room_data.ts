import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportRoomDataInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerExportRoomDataTool(server: McpServer): void {
  server.tool("export_room_data", "Export room data with area, volume, and department info", ExportRoomDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_room_data", args);
      });
      logToolCall({ tool: "export_room_data", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "export_room_data", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
