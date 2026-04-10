import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const GetScheduleDataInput = z.object({
  scheduleId: z
    .number()
    .optional()
    .default(0)
    .describe("Schedule ID. 0 or omit to list all schedules."),
  maxRows: z
    .number()
    .int()
    .optional()
    .default(500)
    .describe("Max rows to return. Default: 500"),
});

export function registerGetScheduleDataTool(server: McpServer): void {
  server.tool(
    "get_schedule_data",
    "List all schedules (omit scheduleId) or retrieve headers and rows for a specific schedule.",
    GetScheduleDataInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_schedule_data", args);
        });
        logToolCall({ tool: "get_schedule_data", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_schedule_data", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
