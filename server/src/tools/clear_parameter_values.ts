import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ClearParameterValuesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerClearParameterValuesTool(server: McpServer): void {
  server.tool("clear_parameter_values", "Clear parameter values on elements by category or scope", ClearParameterValuesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("clear_parameter_values", args);
      });
      return toolResponse("clear_parameter_values", result, Date.now() - start, args);
    } catch (error) {
      return toolError("clear_parameter_values", error, Date.now() - start);
    }
  });
}
