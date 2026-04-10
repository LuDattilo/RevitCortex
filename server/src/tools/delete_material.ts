import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const DeleteMaterialInput = z.object({
  materialId: z.number().optional().describe("Material element ID to delete"),
  materialName: z.string().optional().describe("Material name to delete (case-insensitive)"),
});

export function registerDeleteMaterialTool(server: McpServer): void {
  server.tool(
    "delete_material",
    "Delete a material from the project. Shows confirmation dialog before executing.",
    DeleteMaterialInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("delete_material", args);
        });
        return toolResponse("delete_material", result, Date.now() - start, args);
      } catch (error) {
        return toolError("delete_material", error, Date.now() - start);
      }
    }
  );
}
