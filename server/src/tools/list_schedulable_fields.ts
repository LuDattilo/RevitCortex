import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

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
        logToolCall({ tool: "list_schedulable_fields", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "list_schedulable_fields", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
