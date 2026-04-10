import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

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
        return toolResponse("get_material_properties", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_material_properties", error, Date.now() - start);
      }
    }
  );
}
