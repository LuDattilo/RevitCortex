import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ClearParameterValuesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerClearParameterValuesTool(server: McpServer): void {
  server.tool("clear_parameter_values", "Clear parameter values on elements by category or scope", ClearParameterValuesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("clear_parameter_values", args);
      });
      logToolCall({ tool: "clear_parameter_values", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "clear_parameter_values", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
