import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { RenumberElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerRenumberElementsTool(server: McpServer): void {
  server.tool("renumber_elements", "Renumber rooms/doors/windows by location or name. dryRun=true by default.", RenumberElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("renumber_elements", args);
      });
      logToolCall({ tool: "renumber_elements", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "renumber_elements", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
