import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetMaterialPropertiesInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSetMaterialPropertiesTool(server: McpServer): void {
  server.tool("set_material_properties", "Set identity and product info on Revit materials", SetMaterialPropertiesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("set_material_properties", args);
      });
      logToolCall({ tool: "set_material_properties", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "set_material_properties", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
