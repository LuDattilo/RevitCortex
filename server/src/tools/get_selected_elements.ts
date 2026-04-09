import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetSelectedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetSelectedElementsTool(server: McpServer): void {
  server.tool(
    "get_selected_elements",
    "Get info about currently selected elements in Revit",
    GetSelectedElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_selected_elements", args);
        });
        logToolCall({ tool: "get_selected_elements", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_selected_elements", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
