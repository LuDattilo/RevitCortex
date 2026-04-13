import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcListExportConfigurationsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcListExportConfigurationsTool(server: McpServer): void {
  server.tool(
    "ifc_list_export_configurations",
    "List available IFC export configurations (built-in presets like IFC4 Reference View, IFC2x3 CV2, etc.).",
    IfcListExportConfigurationsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_list_export_configurations", args);
        });
        return toolResponse("ifc_list_export_configurations", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_list_export_configurations", error, Date.now() - start);
      }
    }
  );
}
