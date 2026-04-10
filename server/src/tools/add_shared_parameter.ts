import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AddSharedParameterInput } from "../schemas/parameters.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAddSharedParameterTool(server: McpServer): void {
  server.tool("add_shared_parameter", "Add a shared parameter to project categories", AddSharedParameterInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("add_shared_parameter", args);
      });
      return toolResponse("add_shared_parameter", result, Date.now() - start, args);
    } catch (error) {
      return toolError("add_shared_parameter", error, Date.now() - start);
    }
  });
}
