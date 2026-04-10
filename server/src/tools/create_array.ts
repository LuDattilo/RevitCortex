import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateArrayInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateArrayTool(server: McpServer): void {
  server.tool("create_array", "Create linear or radial arrays of elements", CreateArrayInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_array", args);
      });
      return toolResponse("create_array", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_array", error, Date.now() - start);
    }
  });
}
