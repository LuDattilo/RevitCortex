import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetProjectInfoInput } from "../schemas/project.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

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
        logToolCall({ tool: "get_project_info", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_project_info", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
