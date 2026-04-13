import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcRebuildFamilyInstancesInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcRebuildFamilyInstancesTool(server: McpServer): void {
  server.tool(
    "ifc_rebuild_family_instances",
    "Rebuild doors, windows, and other family instances from IFC DirectShapes.",
    IfcRebuildFamilyInstancesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_rebuild_family_instances", args);
        });
        return toolResponse("ifc_rebuild_family_instances", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_rebuild_family_instances", error, Date.now() - start);
      }
    }
  );
}
