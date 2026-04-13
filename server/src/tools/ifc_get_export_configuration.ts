import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcGetExportConfigurationInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcGetExportConfigurationTool(server: McpServer): void {
  server.tool(
    "ifc_get_export_configuration",
    "Get the full details and option set of a specific IFC export configuration.",
    IfcGetExportConfigurationInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_get_export_configuration", args);
        });
        return toolResponse("ifc_get_export_configuration", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_get_export_configuration", error, Date.now() - start);
      }
    }
  );
}
