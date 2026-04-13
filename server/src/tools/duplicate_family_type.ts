import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const DuplicateFamilyTypeInput = z.object({
  sourceTypeId: z.number().optional().describe("Source family type element ID"),
  sourceTypeName: z.string().optional().describe("Source type name (case-insensitive)"),
  familyName: z.string().optional().describe("Family name to disambiguate when multiple families share the same type name"),
  newName: z.string().describe("Name for the duplicated type"),
  parameterOverrides: z.record(z.union([z.string(), z.number(), z.boolean()])).optional()
    .describe("Type parameters to set on the new type, e.g. {\"Width\": 800, \"Height\": 2100}"),
});

export function registerDuplicateFamilyTypeTool(server: McpServer): void {
  server.tool(
    "duplicate_family_type",
    "Duplicate a loadable family type (door, window, furniture, etc.) with a new name. Optionally sets type parameters on the new type in the same operation.",
    DuplicateFamilyTypeInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("duplicate_family_type", args);
        });
        return toolResponse("duplicate_family_type", result, Date.now() - start, args);
      } catch (error) {
        return toolError("duplicate_family_type", error, Date.now() - start);
      }
    }
  );
}
