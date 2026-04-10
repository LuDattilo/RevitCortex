import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateGridInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateGridTool(server: McpServer): void {
  server.tool("create_grid", "Create a grid system with configurable spacing and labels", CreateGridInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_grid", args);
      });
      return toolResponse("create_grid", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_grid", error, Date.now() - start);
    }
  });
}
