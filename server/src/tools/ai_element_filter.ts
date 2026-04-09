import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AIElementFilterInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerAIElementFilterTool(server: McpServer): void {
  server.tool(
    "ai_element_filter",
    "Intelligent Revit element query. Filter by category (OST_*), type, instances. Returns element IDs, names, categories.",
    AIElementFilterInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ai_element_filter", args);
        });
        logToolCall({ tool: "ai_element_filter", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "ai_element_filter", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
