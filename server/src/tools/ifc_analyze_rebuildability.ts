import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcAnalyzeRebuildabilityInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcAnalyzeRebuildabilityTool(server: McpServer): void {
  server.tool(
    "ifc_analyze_rebuildability",
    "Analyze IFC-imported elements for native Revit reconstruction feasibility.",
    IfcAnalyzeRebuildabilityInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_analyze_rebuildability", args);
        });
        return toolResponse("ifc_analyze_rebuildability", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_analyze_rebuildability", error, Date.now() - start);
      }
    }
  );
}
