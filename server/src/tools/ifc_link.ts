import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcLinkInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcLinkTool(server: McpServer): void {
  server.tool(
    "ifc_link",
    "Link an IFC file into the active Revit document. Creates an intermediate .ifc.RVT and a RevitLinkInstance.",
    IfcLinkInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_link", args);
        });
        return toolResponse("ifc_link", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_link", error, Date.now() - start);
      }
    }
  );
}
