import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { TransferParametersInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerTransferParametersTool(server: McpServer): void {
  server.tool("transfer_parameters", "Copy parameter values from source to target elements", TransferParametersInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("transfer_parameters", args);
      });
      return toolResponse("transfer_parameters", result, Date.now() - start, args);
    } catch (error) {
      return toolError("transfer_parameters", error, Date.now() - start);
    }
  });
}
