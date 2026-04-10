import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const ListSchedulableFieldsInput = z.object({
  categoryName: z
    .string()
    .optional()
    .default("OST_Rooms")
    .describe("BuiltInCategory code (e.g. OST_Rooms, OST_Walls). Default: OST_Rooms"),
  scheduleType: z
    .enum(["regular", "material_takeoff", "key_schedule"])
    .optional()
    .default("regular")
    .describe("Schedule type. Default: regular"),
});

export function registerListSchedulableFieldsTool(server: McpServer): void {
  server.tool(
    "list_schedulable_fields",
    "Discover all available schedulable fields for a given category and schedule type.",
    ListSchedulableFieldsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("list_schedulable_fields", args);
        });
        return toolResponse("list_schedulable_fields", result, Date.now() - start, args);
      } catch (error) {
        return toolError("list_schedulable_fields", error, Date.now() - start);
      }
    }
  );
}
