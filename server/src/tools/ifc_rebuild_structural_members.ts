import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcRebuildStructuralMembersInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcRebuildStructuralMembersTool(server: McpServer): void {
  server.tool(
    "ifc_rebuild_structural_members",
    "Rebuild native Revit columns and beams from IFC-imported DirectShape elements.",
    IfcRebuildStructuralMembersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_rebuild_structural_members", args);
        });
        return toolResponse("ifc_rebuild_structural_members", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_rebuild_structural_members", error, Date.now() - start);
      }
    }
  );
}
