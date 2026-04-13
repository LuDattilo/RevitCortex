import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcRebuildOpeningsInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcRebuildOpeningsTool(server: McpServer): void {
  server.tool(
    "ifc_rebuild_openings",
    "Cut openings in rebuilt walls/floors based on IFC opening elements.",
    IfcRebuildOpeningsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_rebuild_openings", args);
        });
        return toolResponse("ifc_rebuild_openings", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_rebuild_openings", error, Date.now() - start);
      }
    }
  );
}
