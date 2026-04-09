import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { FindUndimensionedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerFindUndimensionedElementsTool(server: McpServer): void {
  server.tool("find_undimensioned_elements", "QA audit: find elements not referenced by dimensions", FindUndimensionedElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("find_undimensioned_elements", args);
      });
      logToolCall({ tool: "find_undimensioned_elements", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "find_undimensioned_elements", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
