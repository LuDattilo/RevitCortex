import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AIElementFilterInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

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
        return toolResponse("ai_element_filter", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ai_element_filter", error, Date.now() - start);
      }
    }
  );
}
