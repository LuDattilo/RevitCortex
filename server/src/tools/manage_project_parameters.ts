import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageProjectParametersInput } from "../schemas/parameters.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerManageProjectParametersTool(server: McpServer): void {
  server.tool("manage_project_parameters", "List, create, delete, or modify project parameters", ManageProjectParametersInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_project_parameters", args);
      });
      logToolCall({ tool: "manage_project_parameters", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "manage_project_parameters", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
