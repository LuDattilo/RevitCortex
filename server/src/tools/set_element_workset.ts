import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementWorksetInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSetElementWorksetTool(server: McpServer): void {
  server.tool("set_element_workset", "Move elements to a different workset", SetElementWorksetInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("set_element_workset", args);
      });
      return toolResponse("set_element_workset", result, Date.now() - start, args);
    } catch (error) {
      return toolError("set_element_workset", error, Date.now() - start);
    }
  });
}
