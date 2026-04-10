import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SectionBoxFromSelectionInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSectionBoxFromSelectionTool(server: McpServer): void {
  server.tool("section_box_from_selection", "Create a 3D section box from selected elements", SectionBoxFromSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("section_box_from_selection", args);
      });
      logToolCall({ tool: "section_box_from_selection", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "section_box_from_selection", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
