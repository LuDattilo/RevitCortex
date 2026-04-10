import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { RenameFamiliesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerRenameFamiliesTool(server: McpServer): void {
  server.tool("rename_families", "Rename loaded families with find/replace, prefix, or suffix", RenameFamiliesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("rename_families", args);
      });
      logToolCall({ tool: "rename_families", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "rename_families", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
