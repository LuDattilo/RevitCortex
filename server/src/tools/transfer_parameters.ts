import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { TransferParametersInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerTransferParametersTool(server: McpServer): void {
  server.tool("transfer_parameters", "Copy parameter values from source to target elements", TransferParametersInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("transfer_parameters", args);
      });
      logToolCall({ tool: "transfer_parameters", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "transfer_parameters", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
