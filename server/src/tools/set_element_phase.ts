import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementPhaseInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSetElementPhaseTool(server: McpServer): void {
  server.tool("set_element_phase", "Assign created/demolished phase to elements", SetElementPhaseInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("set_element_phase", args);
      });
      logToolCall({ tool: "set_element_phase", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "set_element_phase", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
