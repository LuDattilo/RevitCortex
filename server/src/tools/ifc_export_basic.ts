import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcExportBasicInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcExportBasicTool(server: McpServer): void {
  server.tool(
    "ifc_export_basic",
    "Export the active Revit document to IFC with standard options (version, view filter, base quantities).",
    IfcExportBasicInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_export_basic", args);
        });
        return toolResponse("ifc_export_basic", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_export_basic", error, Date.now() - start);
      }
    }
  );
}
