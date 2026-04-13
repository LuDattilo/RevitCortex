import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcRebuildFloorsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcRebuildFloorsTool(server: McpServer): void {
  server.tool(
    "ifc_rebuild_floors",
    "Rebuild native Revit floors from IFC-imported DirectShape elements.",
    IfcRebuildFloorsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_rebuild_floors", args);
        });
        return toolResponse("ifc_rebuild_floors", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_rebuild_floors", error, Date.now() - start);
      }
    }
  );
}
