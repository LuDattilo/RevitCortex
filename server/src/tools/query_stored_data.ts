import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { QueryStoredDataInput } from "../schemas/database.js";
import { getAllProjects, getProjectById, getProjectByName, getRoomsByProjectId, getAllRoomsWithProject, getStats } from "../database/service.js";
import { logToolCall } from "../logging/logger.js";

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
            logToolCall({ tool: "query_stored_data", success: true, durationMs: Date.now() - start });
            return { content: [{ type: "text" as const, text: JSON.stringify({ success: false, error: `Project with ID ${args.project_id} not found` }, null, 2) }] };
          }
          break;
        case "project_by_name":
          if (!args.project_name) throw new Error("project_name is required for this query type");
          data = getProjectByName(args.project_name);
          if (!data) {
            logToolCall({ tool: "query_stored_data", success: true, durationMs: Date.now() - start });
            return { content: [{ type: "text" as const, text: JSON.stringify({ success: false, error: `Project "${args.project_name}" not found` }, null, 2) }] };
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
            logToolCall({ tool: "query_stored_data", success: true, durationMs: Date.now() - start });
            return { content: [{ type: "text" as const, text: JSON.stringify({ success: false, error: `Project "${args.project_name}" not found` }, null, 2) }] };
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

      logToolCall({ tool: "query_stored_data", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify({ success: true, query_type: args.query_type, data }, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "query_stored_data", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
