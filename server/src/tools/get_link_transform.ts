import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetLinkTransformInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetLinkTransformTool(server: McpServer): void {
  server.tool("get_link_transform", "Returns the full transform of a linked file instance: origin (mm), basis vectors, and rotation angle (degrees)", GetLinkTransformInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("get_link_transform", args);
      });
      return toolResponse("get_link_transform", result, Date.now() - start, args);
    } catch (error) {
      return toolError("get_link_transform", error, Date.now() - start);
    }
  });
}
