import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AddPrefixSuffixInput } from "../schemas/parameters.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerAddPrefixSuffixTool(server: McpServer): void {
  server.tool("add_prefix_suffix", "Add prefix/suffix to parameter values with dry-run preview", AddPrefixSuffixInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("add_prefix_suffix", args);
      });
      logToolCall({ tool: "add_prefix_suffix", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "add_prefix_suffix", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
