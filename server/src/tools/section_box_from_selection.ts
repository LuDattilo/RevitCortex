import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SectionBoxFromSelectionInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSectionBoxFromSelectionTool(server: McpServer): void {
  server.tool("section_box_from_selection", "Create a 3D section box from selected elements", SectionBoxFromSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("section_box_from_selection", args);
      });
      return toolResponse("section_box_from_selection", result, Date.now() - start, args);
    } catch (error) {
      return toolError("section_box_from_selection", error, Date.now() - start);
    }
  });
}
