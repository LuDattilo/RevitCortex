import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { MatchElementPropertiesInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerMatchElementPropertiesTool(server: McpServer): void {
  server.tool("match_element_properties", "Copy parameter values from source to target elements", MatchElementPropertiesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("match_element_properties", args);
      });
      logToolCall({ tool: "match_element_properties", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "match_element_properties", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
