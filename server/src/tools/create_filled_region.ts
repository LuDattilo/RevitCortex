import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateFilledRegionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateFilledRegionTool(server: McpServer): void {
  server.tool("create_filled_region", "Create a filled region from boundary points", CreateFilledRegionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_filled_region", args);
      });
      return toolResponse("create_filled_region", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_filled_region", error, Date.now() - start);
    }
  });
}
