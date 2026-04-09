import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetElementParametersInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetElementParametersTool(server: McpServer): void {
  server.tool(
    "get_element_parameters",
    "Get all parameters (instance and type) of one or more Revit elements by their IDs.",
    GetElementParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_element_parameters", args);
        });
        logToolCall({ tool: "get_element_parameters", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_element_parameters", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
