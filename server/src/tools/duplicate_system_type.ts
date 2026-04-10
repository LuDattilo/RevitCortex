import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const DuplicateSystemTypeInput = z.object({
  sourceTypeId: z.number().optional().describe("Source type element ID"),
  sourceTypeName: z.string().optional().describe("Source type name (case-insensitive)"),
  category: z.string().optional().describe("Category filter (e.g. OST_Floors, OST_Walls)"),
  newName: z.string().describe("Name for the duplicated type"),
});

export function registerDuplicateSystemTypeTool(server: McpServer): void {
  server.tool(
    "duplicate_system_type",
    "Duplicates a system family type (wall, floor, roof, ceiling) with a new name. Returns the new type ID.",
    DuplicateSystemTypeInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("duplicate_system_type", args);
        });
        return toolResponse("duplicate_system_type", result, Date.now() - start, args);
      } catch (error) {
        return toolError("duplicate_system_type", error, Date.now() - start);
      }
    }
  );
}
