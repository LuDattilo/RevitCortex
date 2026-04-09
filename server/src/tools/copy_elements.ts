import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CopyElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCopyElementsTool(server: McpServer): void {
  server.tool("copy_elements", "Copy elements with optional offset and view-to-view support", CopyElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("copy_elements", args);
      });
      logToolCall({ tool: "copy_elements", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "copy_elements", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
