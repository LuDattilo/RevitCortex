import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const GetCompoundStructureInput = z.object({
  elementId: z.number().optional().describe("Element instance ID (wall, floor, roof, ceiling)"),
  typeId: z.number().optional().describe("Type ID of a system family type (WallType, FloorType, etc.)"),
  typeName: z.string().optional().describe("Type name to search (case-insensitive)"),
  category: z.string().optional().describe("Category filter when using typeName (e.g. OST_Walls, OST_Floors, OST_Roofs, OST_Ceilings)"),
});

export function registerGetCompoundStructureTool(server: McpServer): void {
  server.tool(
    "get_compound_structure",
    "Read compound structure (layer stratigraphy) from system family types: walls, floors, roofs, ceilings. Returns layer function, width (mm/ft), and material for each layer.",
    GetCompoundStructureInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_compound_structure", args);
        });
        return toolResponse("get_compound_structure", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_compound_structure", error, Date.now() - start);
      }
    }
  );
}
