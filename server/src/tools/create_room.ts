import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateRoomInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateRoomTool(server: McpServer): void {
  server.tool("create_room", "Create a room at specified location", CreateRoomInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_room", args);
      });
      logToolCall({ tool: "create_room", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_room", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
