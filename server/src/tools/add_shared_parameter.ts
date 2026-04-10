import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AddSharedParameterInput } from "../schemas/parameters.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerAddSharedParameterTool(server: McpServer): void {
  server.tool("add_shared_parameter", "Add a shared parameter to project categories", AddSharedParameterInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("add_shared_parameter", args);
      });
      logToolCall({ tool: "add_shared_parameter", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "add_shared_parameter", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
