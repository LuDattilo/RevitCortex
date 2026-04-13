import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcOpenOrImportInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcOpenOrImportTool(server: McpServer): void {
  server.tool(
    "ifc_open_or_import",
    "Open or import an IFC file into Revit. 'open' creates a new document; 'link' creates a reference.",
    IfcOpenOrImportInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_open_or_import", args);
        });
        return toolResponse("ifc_open_or_import", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_open_or_import", error, Date.now() - start);
      }
    }
  );
}
