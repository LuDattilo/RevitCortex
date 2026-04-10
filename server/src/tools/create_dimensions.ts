import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateDimensionsInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateDimensionsTool(server: McpServer): void {
  server.tool("create_dimensions", "Create dimension annotations between points or elements", CreateDimensionsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_dimensions", args);
      });
      return toolResponse("create_dimensions", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_dimensions", error, Date.now() - start);
    }
  });
}
