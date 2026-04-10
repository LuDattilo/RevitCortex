import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const DuplicateMaterialInput = z.object({
  sourceMaterialId: z.number().optional().describe("Source material element ID"),
  sourceMaterialName: z.string().optional().describe("Source material name (case-insensitive)"),
  newName: z.string().describe("Name for the duplicated material"),
});

export function registerDuplicateMaterialTool(server: McpServer): void {
  server.tool(
    "duplicate_material",
    "Duplicate an existing material with a new name, copying all properties and assets.",
    DuplicateMaterialInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("duplicate_material", args);
        });
        return toolResponse("duplicate_material", result, Date.now() - start, args);
      } catch (error) {
        return toolError("duplicate_material", error, Date.now() - start);
      }
    }
  );
}
