import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementWorksetInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSetElementWorksetTool(server: McpServer): void {
  server.tool("set_element_workset", "Move elements to a different workset", SetElementWorksetInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("set_element_workset", args);
      });
      logToolCall({ tool: "set_element_workset", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "set_element_workset", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
