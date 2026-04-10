import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ExportRoomDataInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerExportRoomDataTool(server: McpServer): void {
  server.tool("export_room_data", "Export room data with area, volume, and department info", ExportRoomDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("export_room_data", args);
      });
      return toolResponse("export_room_data", result, Date.now() - start, args);
    } catch (error) {
      return toolError("export_room_data", error, Date.now() - start);
    }
  });
}
