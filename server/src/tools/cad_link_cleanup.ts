import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CadLinkCleanupInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCadLinkCleanupTool(server: McpServer): void {
  server.tool("cad_link_cleanup", "Analyze and clean up imported/linked CAD files", CadLinkCleanupInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("cad_link_cleanup", args);
      });
      logToolCall({ tool: "cad_link_cleanup", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "cad_link_cleanup", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
