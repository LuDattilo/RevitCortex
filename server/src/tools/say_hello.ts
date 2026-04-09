import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SayHelloInput } from "../schemas/meta.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSayHelloTool(server: McpServer): void {
  server.tool(
    "say_hello",
    "Test MCP connection to RevitCortex. Displays a greeting.",
    SayHelloInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("say_hello", args);
        });
        logToolCall({ tool: "say_hello", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "say_hello", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
