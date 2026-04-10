import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AnalyzeModelStatisticsInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerAnalyzeModelStatisticsTool(server: McpServer): void {
  server.tool("analyze_model_statistics", "Analyze model complexity with element counts and category breakdown", AnalyzeModelStatisticsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("analyze_model_statistics", args);
      });
      logToolCall({ tool: "analyze_model_statistics", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "analyze_model_statistics", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
