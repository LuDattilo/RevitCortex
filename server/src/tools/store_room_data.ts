import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StoreRoomDataInput } from "../schemas/database.js";
import { storeRoomsBatch, getProjectByName, getRoomsByProjectId } from "../database/service.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerStoreRoomDataTool(server: McpServer): void {
  server.tool("store_room_data", "Store or update room data for a project in the local RevitCortex database", StoreRoomDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const project = getProjectByName(args.project_name);
      if (!project) {
        return toolError("store_room_data", new Error(`Project "${args.project_name}" not found. Use store_project_data first.`), Date.now() - start);
      }

      const count = storeRoomsBatch(project.id, args.rooms);
      const rooms = getRoomsByProjectId(project.id);
      return toolResponse("store_room_data", { success: true, project_id: project.id, project_name: args.project_name, rooms_stored: count, total_rooms: rooms.length }, Date.now() - start, args);
    } catch (error) {
      return toolError("store_room_data", error, Date.now() - start);
    }
  });
}
