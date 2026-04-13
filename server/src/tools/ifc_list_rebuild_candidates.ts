import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcListRebuildCandidatesInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcListRebuildCandidatesTool(server: McpServer): void {
  server.tool(
    "ifc_list_rebuild_candidates",
    "List IFC elements that can be rebuilt as native Revit elements above a confidence threshold.",
    IfcListRebuildCandidatesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_list_rebuild_candidates", args);
        });
        return toolResponse("ifc_list_rebuild_candidates", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_list_rebuild_candidates", error, Date.now() - start);
      }
    }
  );
}
