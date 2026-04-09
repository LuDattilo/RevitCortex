import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ModifyElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerModifyElementTool(server: McpServer): void {
  server.tool(
    "modify_element",
    "Move, rotate, mirror, or copy elements. Coordinates in mm.",
    ModifyElementInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("modify_element", args);
        });
        logToolCall({ tool: "modify_element", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "modify_element", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
