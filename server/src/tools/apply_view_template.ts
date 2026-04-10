import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ApplyViewTemplateInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerApplyViewTemplateTool(server: McpServer): void {
  server.tool("apply_view_template", "List, apply, or remove view templates from views", ApplyViewTemplateInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("apply_view_template", args);
      });
      return toolResponse("apply_view_template", result, Date.now() - start, args);
    } catch (error) {
      return toolError("apply_view_template", error, Date.now() - start);
    }
  });
}
