import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetWorksetsInput } from "../schemas/project.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetWorksetsTool(server: McpServer): void {
  server.tool(
    "get_worksets",
    "List worksets with open/close status and ownership. Only available for workshared documents.",
    GetWorksetsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_worksets", args);
        });
        logToolCall({ tool: "get_worksets", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_worksets", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
