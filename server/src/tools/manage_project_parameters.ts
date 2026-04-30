import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageProjectParametersInput } from "../schemas/parameters.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerManageProjectParametersTool(server: McpServer): void {
  server.tool("manage_project_parameters", "List, create, delete, modify, or set_group on project parameters. set_group bulk-changes the 'Group Parameter Under' assignment for user-defined parameters.", ManageProjectParametersInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_project_parameters", args);
      });
      return toolResponse("manage_project_parameters", result, Date.now() - start, args);
    } catch (error) {
      return toolError("manage_project_parameters", error, Date.now() - start);
    }
  });
}
