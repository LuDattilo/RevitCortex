import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ApplyViewTemplateInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerApplyViewTemplateTool(server: McpServer): void {
  server.tool("apply_view_template", "List, apply, or remove view templates from views", ApplyViewTemplateInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("apply_view_template", args);
      });
      logToolCall({ tool: "apply_view_template", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "apply_view_template", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
