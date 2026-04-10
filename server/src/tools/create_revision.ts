import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateRevisionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateRevisionTool(server: McpServer): void {
  server.tool("create_revision", "List create or assign revisions to sheets", CreateRevisionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_revision", args);
      });
      return toolResponse("create_revision", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_revision", error, Date.now() - start);
    }
  });
}
