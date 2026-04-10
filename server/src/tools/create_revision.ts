import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateRevisionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateRevisionTool(server: McpServer): void {
  server.tool("create_revision", "List create or assign revisions to sheets", CreateRevisionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_revision", args);
      });
      logToolCall({ tool: "create_revision", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_revision", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
