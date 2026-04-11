import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { MoveLinkInstanceInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerMoveLinkInstanceTool(server: McpServer): void {
  server.tool("move_link_instance", "Moves a linked file instance by a delta offset (mm) or to an absolute position (mm)", MoveLinkInstanceInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("move_link_instance", args);
      });
      return toolResponse("move_link_instance", result, Date.now() - start, args);
    } catch (error) {
      return toolError("move_link_instance", error, Date.now() - start);
    }
  });
}
