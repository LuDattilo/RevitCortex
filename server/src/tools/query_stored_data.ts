import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { QueryStoredDataInput } from "../schemas/database.js";
import { getAllProjects, getProjectById, getProjectByName, getRoomsByProjectId, getAllRoomsWithProject, getStats } from "../database/service.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerQueryStoredDataTool(server: McpServer): void {
  server.tool("query_stored_data", "Query projects and rooms stored in the local RevitCortex database", QueryStoredDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      let data: unknown;

      switch (args.query_type) {
        case "all_projects":
          data = getAllProjects();
          break;
        case "project_by_id":
          if (!args.project_id) throw new Error("project_id is required for this query type");
          data = getProjectById(args.project_id);
          if (!data) {
            return toolError("query_stored_data", new Error(`Project with ID ${args.project_id} not found`), Date.now() - start);
          }
          break;
        case "project_by_name":
          if (!args.project_name) throw new Error("project_name is required for this query type");
          data = getProjectByName(args.project_name);
          if (!data) {
            return toolError("query_stored_data", new Error(`Project "${args.project_name}" not found`), Date.now() - start);
          }
          break;
        case "rooms_by_project_id":
          if (!args.project_id) throw new Error("project_id is required for this query type");
          data = getRoomsByProjectId(args.project_id);
          break;
        case "rooms_by_project_name":
          if (!args.project_name) throw new Error("project_name is required for this query type");
          const project = getProjectByName(args.project_name);
          if (!project) {
            return toolError("query_stored_data", new Error(`Project "${args.project_name}" not found`), Date.now() - start);
          }
          data = getRoomsByProjectId(project.id);
          break;
        case "all_rooms":
          data = getAllRoomsWithProject();
          break;
        case "stats":
          data = getStats();
          break;
      }

      return toolResponse("query_stored_data", { success: true, query_type: args.query_type, data }, Date.now() - start, args);
    } catch (error) {
      return toolError("query_stored_data", error, Date.now() - start);
    }
  });
}
