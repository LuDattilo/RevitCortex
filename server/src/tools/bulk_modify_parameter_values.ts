import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BulkModifyParameterValuesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerBulkModifyParameterValuesTool(server: McpServer): void {
  server.tool("bulk_modify_parameter_values", "Bulk set, prefix, suffix, find/replace, or clear parameter values", BulkModifyParameterValuesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("bulk_modify_parameter_values", args);
      });
      return toolResponse("bulk_modify_parameter_values", result, Date.now() - start, args);
    } catch (error) {
      return toolError("bulk_modify_parameter_values", error, Date.now() - start);
    }
  });
}
