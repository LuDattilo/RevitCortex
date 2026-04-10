import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetMaterialPropertiesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSetMaterialPropertiesTool(server: McpServer): void {
  server.tool("set_material_properties", "Set identity and product info on Revit materials", SetMaterialPropertiesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("set_material_properties", args);
      });
      return toolResponse("set_material_properties", result, Date.now() - start, args);
    } catch (error) {
      return toolError("set_material_properties", error, Date.now() - start);
    }
  });
}
