import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AlignViewportsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerAlignViewportsTool(server: McpServer): void {
  server.tool("align_viewports", "Align viewports across sheets by position", AlignViewportsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("align_viewports", args);
      });
      logToolCall({ tool: "align_viewports", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "align_viewports", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
