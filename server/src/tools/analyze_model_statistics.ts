import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AnalyzeModelStatisticsInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAnalyzeModelStatisticsTool(server: McpServer): void {
  server.tool("analyze_model_statistics", "Analyze model complexity with element counts and category breakdown", AnalyzeModelStatisticsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("analyze_model_statistics", args);
      });
      return toolResponse("analyze_model_statistics", result, Date.now() - start, args);
    } catch (error) {
      return toolError("analyze_model_statistics", error, Date.now() - start);
    }
  });
}
