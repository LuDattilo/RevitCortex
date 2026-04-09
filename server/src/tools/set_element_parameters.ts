import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementParametersInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSetElementParametersTool(server: McpServer): void {
  server.tool(
    "set_element_parameters",
    "Set parameter values on one or more Revit elements. Supports string, number, and boolean values.",
    SetElementParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("set_element_parameters", args);
        });
        logToolCall({ tool: "set_element_parameters", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "set_element_parameters", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
