import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcSetFamilyMappingFileInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcSetFamilyMappingFileTool(server: McpServer): void {
  server.tool(
    "ifc_set_family_mapping_file",
    "Set the family mapping file for IFC exports. Persists in session for subsequent export calls.",
    IfcSetFamilyMappingFileInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_set_family_mapping_file", args);
        });
        return toolResponse("ifc_set_family_mapping_file", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_set_family_mapping_file", error, Date.now() - start);
      }
    }
  );
}
