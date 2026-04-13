import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcReloadLinkInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcReloadLinkTool(server: McpServer): void {
  server.tool(
    "ifc_reload_link",
    "Reload an existing IFC link, optionally from a new IFC file path.",
    IfcReloadLinkInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_reload_link", args);
        });
        return toolResponse("ifc_reload_link", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_reload_link", error, Date.now() - start);
      }
    }
  );
}
