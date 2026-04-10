import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetProjectInfoInput } from "../schemas/project.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetProjectInfoTool(server: McpServer): void {
  server.tool(
    "get_project_info",
    "Get comprehensive project information from the active Revit document including metadata, phases, worksets, links, and levels. Call this first in a new project to understand its structure.",
    GetProjectInfoInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_project_info", args);
        });
        return toolResponse("get_project_info", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_project_info", error, Date.now() - start);
      }
    }
  );
}
