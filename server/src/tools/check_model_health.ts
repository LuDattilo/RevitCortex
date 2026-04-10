import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CheckModelHealthInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCheckModelHealthTool(server: McpServer): void {
  server.tool("check_model_health", "Comprehensive BIM model health audit with score and recommendations", CheckModelHealthInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("check_model_health", args);
      });
      logToolCall({ tool: "check_model_health", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "check_model_health", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
