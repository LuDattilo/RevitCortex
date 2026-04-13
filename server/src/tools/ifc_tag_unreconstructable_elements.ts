import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcTagUnreconstructableElementsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcTagUnreconstructableElementsTool(server: McpServer): void {
  server.tool(
    "ifc_tag_unreconstructable_elements",
    "Tag IFC elements that cannot be rebuilt, marking them for manual review.",
    IfcTagUnreconstructableElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_tag_unreconstructable_elements", args);
        });
        return toolResponse("ifc_tag_unreconstructable_elements", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_tag_unreconstructable_elements", error, Date.now() - start);
      }
    }
  );
}
