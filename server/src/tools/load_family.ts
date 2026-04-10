import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { LoadFamilyInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerLoadFamilyTool(server: McpServer): void {
  server.tool("load_family", "Load .rfa family, list loaded families, or duplicate a type", LoadFamilyInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("load_family", args);
      });
      logToolCall({ tool: "load_family", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "load_family", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
