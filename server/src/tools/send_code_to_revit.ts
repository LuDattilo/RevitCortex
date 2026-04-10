import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SendCodeToRevitInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSendCodeToRevitTool(server: McpServer): void {
  server.tool("send_code_to_revit", "Execute custom C# code in the Revit context", SendCodeToRevitInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("send_code_to_revit", args);
      });
      logToolCall({ tool: "send_code_to_revit", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "send_code_to_revit", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
