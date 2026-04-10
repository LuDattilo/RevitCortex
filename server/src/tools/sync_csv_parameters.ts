import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SyncCsvParametersInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSyncCsvParametersTool(server: McpServer): void {
  server.tool("sync_csv_parameters", "Sync element parameters from structured data", SyncCsvParametersInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("sync_csv_parameters", args);
      });
      logToolCall({ tool: "sync_csv_parameters", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "sync_csv_parameters", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
