import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcRebuildRoofsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcRebuildRoofsTool(server: McpServer): void {
  server.tool(
    "ifc_rebuild_roofs",
    "Rebuild native Revit roofs from IFC-imported DirectShape elements.",
    IfcRebuildRoofsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_rebuild_roofs", args);
        });
        return toolResponse("ifc_rebuild_roofs", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_rebuild_roofs", error, Date.now() - start);
      }
    }
  );
}
