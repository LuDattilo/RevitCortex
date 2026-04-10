import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const GetWarningsInput = z.object({
  severityFilter: z
    .enum(["All", "Warning", "Error"])
    .optional()
    .default("All")
    .describe("Filter by severity. Default: All"),
  maxWarnings: z
    .number()
    .int()
    .optional()
    .default(500)
    .describe("Max warnings to return. Default: 500"),
  categoryFilter: z
    .string()
    .optional()
    .default("")
    .describe("Substring filter on warning description (case-insensitive)"),
});

export function registerGetWarningsTool(server: McpServer): void {
  server.tool(
    "get_warnings",
    "Retrieve model warnings and errors with optional severity/description filtering. Useful for model health auditing.",
    GetWarningsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_warnings", args);
        });
        logToolCall({ tool: "get_warnings", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_warnings", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
