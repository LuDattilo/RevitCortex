import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StoreRoomDataInput } from "../schemas/database.js";
import { storeRoomsBatch, getProjectByName, getRoomsByProjectId } from "../database/service.js";
import { logToolCall } from "../logging/logger.js";

export function registerStoreRoomDataTool(server: McpServer): void {
  server.tool("store_room_data", "Store or update room data for a project in the local RevitCortex database", StoreRoomDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const project = getProjectByName(args.project_name);
      if (!project) {
        logToolCall({ tool: "store_room_data", success: false, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify({ success: false, error: `Project "${args.project_name}" not found. Use store_project_data first.` }, null, 2) }], isError: true };
      }

      const count = storeRoomsBatch(project.id, args.rooms);
      const rooms = getRoomsByProjectId(project.id);
      logToolCall({ tool: "store_room_data", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify({ success: true, project_id: project.id, project_name: args.project_name, rooms_stored: count, total_rooms: rooms.length }, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "store_room_data", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
