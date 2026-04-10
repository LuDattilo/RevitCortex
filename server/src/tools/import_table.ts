import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ImportTableInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerImportTableTool(server: McpServer): void {
  server.tool("import_table", "Import CSV/TSV file as a formatted table in a drafting or legend view", ImportTableInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("import_table", args);
      });
      logToolCall({ tool: "import_table", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "import_table", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
