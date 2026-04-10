import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BulkModifyParameterValuesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerBulkModifyParameterValuesTool(server: McpServer): void {
  server.tool("bulk_modify_parameter_values", "Bulk set, prefix, suffix, find/replace, or clear parameter values", BulkModifyParameterValuesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("bulk_modify_parameter_values", args);
      });
      logToolCall({ tool: "bulk_modify_parameter_values", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "bulk_modify_parameter_values", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
