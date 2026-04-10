import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CheckModelHealthInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCheckModelHealthTool(server: McpServer): void {
  server.tool("check_model_health", "Comprehensive BIM model health audit with score and recommendations", CheckModelHealthInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("check_model_health", args);
      });
      return toolResponse("check_model_health", result, Date.now() - start, args);
    } catch (error) {
      return toolError("check_model_health", error, Date.now() - start);
    }
  });
}
