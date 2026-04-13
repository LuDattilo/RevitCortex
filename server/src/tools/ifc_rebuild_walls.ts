import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcRebuildWallsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcRebuildWallsTool(server: McpServer): void {
  server.tool(
    "ifc_rebuild_walls",
    "Rebuild native Revit walls from IFC-imported DirectShape elements.",
    IfcRebuildWallsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_rebuild_walls", args);
        });
        return toolResponse("ifc_rebuild_walls", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_rebuild_walls", error, Date.now() - start);
      }
    }
  );
}
