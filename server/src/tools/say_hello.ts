import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SayHelloInput } from "../schemas/meta.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

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
        return toolResponse("say_hello", result, Date.now() - start, args);
      } catch (error) {
        return toolError("say_hello", error, Date.now() - start);
      }
    }
  );
}
