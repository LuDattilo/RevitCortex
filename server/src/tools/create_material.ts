import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const CreateMaterialInput = z.object({
  name: z.string().describe("Material name"),
  materialClass: z.string().optional().describe("Material class (e.g. Concrete, Masonry, Metal, Glass)"),
  materialCategory: z.string().optional().describe("Material category"),
  color: z.string().optional().describe("Hex color (e.g. #FF0000)"),
  transparency: z.number().int().min(0).max(100).optional().describe("Transparency 0-100"),
  shininess: z.number().int().min(0).max(128).optional().describe("Shininess 0-128"),
  smoothness: z.number().int().min(0).max(100).optional().describe("Smoothness 0-100"),
});

export function registerCreateMaterialTool(server: McpServer): void {
  server.tool(
    "create_material",
    "Create a new material in the project with name, class, color, transparency, and optional properties.",
    CreateMaterialInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("create_material", args);
        });
        return toolResponse("create_material", result, Date.now() - start, args);
      } catch (error) {
        return toolError("create_material", error, Date.now() - start);
      }
    }
  );
}
