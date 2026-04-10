import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const GetMaterialPropertiesInput = z.object({
  materialId: z
    .number()
    .optional()
    .describe("Revit element ID of the material"),
  materialName: z
    .string()
    .optional()
    .describe("Material name (case-insensitive). Used if materialId not provided."),
});

export function registerGetMaterialPropertiesTool(server: McpServer): void {
  server.tool(
    "get_material_properties",
    "Get detailed physical/thermal properties of a specific material. Provide materialId or materialName.",
    GetMaterialPropertiesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_material_properties", args);
        });
        logToolCall({ tool: "get_material_properties", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_material_properties", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
