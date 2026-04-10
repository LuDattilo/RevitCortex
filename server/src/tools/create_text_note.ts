import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateTextNoteInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateTextNoteTool(server: McpServer): void {
  server.tool("create_text_note", "Create text notes in a view", CreateTextNoteInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_text_note", args);
      });
      return toolResponse("create_text_note", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_text_note", error, Date.now() - start);
    }
  });
}
