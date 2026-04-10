import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StoreProjectDataInput } from "../schemas/database.js";
import { storeProject, getProjectByName } from "../database/service.js";
import { logToolCall } from "../logging/logger.js";

export function registerStoreProjectDataTool(server: McpServer): void {
  server.tool("store_project_data", "Store or update project data in the local RevitCortex database", StoreProjectDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const projectId = storeProject(args);
      const project = getProjectByName(args.project_name);
      logToolCall({ tool: "store_project_data", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify({ success: true, project_id: projectId, project }, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "store_project_data", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
