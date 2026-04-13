import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcGetCapabilitiesInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcGetCapabilitiesTool(server: McpServer): void {
  server.tool(
    "ifc_get_capabilities",
    "Get IFC capabilities: supported versions, import/export availability, revit-ifc add-in detection.",
    IfcGetCapabilitiesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_get_capabilities", args);
        });
        return toolResponse("ifc_get_capabilities", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_get_capabilities", error, Date.now() - start);
      }
    }
  );
}
