import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StoreProjectDataInput } from "../schemas/database.js";
import { storeProject, getProjectByName } from "../database/service.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerStoreProjectDataTool(server: McpServer): void {
  server.tool("store_project_data", "Store or update project data in the local RevitCortex database", StoreProjectDataInput.shape, async (args) => {
    const start = Date.now();
    try {
      const projectId = storeProject(args);
      const project = getProjectByName(args.project_name);
      return toolResponse("store_project_data", { success: true, project_id: projectId, project }, Date.now() - start, args);
    } catch (error) {
      return toolError("store_project_data", error, Date.now() - start);
    }
  });
}
