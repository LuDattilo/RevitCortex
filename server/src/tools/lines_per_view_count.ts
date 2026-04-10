import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const LinesPerViewCountInput = z.object({
  threshold: z
    .number()
    .int()
    .optional()
    .default(0)
    .describe("Only return views with lines >= threshold. Default: 0 (all views)"),
  includeDetailLines: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include detail lines. Default: true"),
  includeModelLines: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include model lines. Default: true"),
  limit: z
    .number()
    .int()
    .optional()
    .default(200)
    .describe("Max views to return. Default: 200"),
});

export function registerLinesPerViewCountTool(server: McpServer): void {
  server.tool(
    "lines_per_view_count",
    "Count detail/model lines per view. Helps identify views with excessive line work impacting performance.",
    LinesPerViewCountInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("lines_per_view_count", args);
        });
        logToolCall({ tool: "lines_per_view_count", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "lines_per_view_count", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
