import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { HighlightLinkedElementInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerHighlightLinkedElementTool(server: McpServer): void {
  server.tool("highlight_linked_element", "Highlights an element inside a linked model: selects the link instance, creates a section box around the target element, and zooms to it", HighlightLinkedElementInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("highlight_linked_element", args);
      });
      return toolResponse("highlight_linked_element", result, Date.now() - start, args);
    } catch (error) {
      return toolError("highlight_linked_element", error, Date.now() - start);
    }
  });
}
