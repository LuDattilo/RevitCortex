import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcCompareOriginalVsRebuiltInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcCompareOriginalVsRebuiltTool(server: McpServer): void {
  server.tool(
    "ifc_compare_original_vs_rebuilt",
    "Compare original IFC element with its rebuilt native Revit counterpart.",
    IfcCompareOriginalVsRebuiltInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_compare_original_vs_rebuilt", args);
        });
        return toolResponse("ifc_compare_original_vs_rebuilt", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_compare_original_vs_rebuilt", error, Date.now() - start);
      }
    }
  );
}
