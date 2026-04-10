import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SyncCsvParametersInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSyncCsvParametersTool(server: McpServer): void {
  server.tool("sync_csv_parameters", "Sync element parameters from structured data", SyncCsvParametersInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("sync_csv_parameters", args);
      });
      return toolResponse("sync_csv_parameters", result, Date.now() - start, args);
    } catch (error) {
      return toolError("sync_csv_parameters", error, Date.now() - start);
    }
  });
}
