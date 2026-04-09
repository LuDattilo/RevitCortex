import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetLinkedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetLinkedElementsTool(server: McpServer): void {
  server.tool(
    "get_linked_elements",
    "Query elements from linked Revit models by category",
    GetLinkedElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_linked_elements", args);
        });
        logToolCall({ tool: "get_linked_elements", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_linked_elements", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
