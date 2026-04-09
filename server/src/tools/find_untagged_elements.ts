import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { FindUntaggedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerFindUntaggedElementsTool(server: McpServer): void {
  server.tool("find_untagged_elements", "QA audit: find elements without tags in a view", FindUntaggedElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("find_untagged_elements", args);
      });
      logToolCall({ tool: "find_untagged_elements", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "find_untagged_elements", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
