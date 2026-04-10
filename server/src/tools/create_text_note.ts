import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateTextNoteInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateTextNoteTool(server: McpServer): void {
  server.tool("create_text_note", "Create text notes in a view", CreateTextNoteInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_text_note", args);
      });
      logToolCall({ tool: "create_text_note", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_text_note", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
