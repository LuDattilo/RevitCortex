import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DeleteElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerDeleteElementTool(server: McpServer): void {
  server.tool(
    "delete_element",
    "Delete elements by ID. Defaults to dryRun=true (preview). Set dryRun=false to delete.",
    DeleteElementInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("delete_element", args);
        });
        logToolCall({ tool: "delete_element", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "delete_element", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
