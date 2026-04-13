import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcExportWithConfigurationInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcExportWithConfigurationTool(server: McpServer): void {
  server.tool(
    "ifc_export_with_configuration",
    "Export to IFC using a named configuration (e.g. 'IFC4 Reference View') with optional key-value overrides.",
    IfcExportWithConfigurationInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_export_with_configuration", args);
        });
        return toolResponse("ifc_export_with_configuration", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_export_with_configuration", error, Date.now() - start);
      }
    }
  );
}
