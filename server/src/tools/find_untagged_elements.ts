import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { FindUntaggedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerFindUntaggedElementsTool(server: McpServer): void {
  server.tool("find_untagged_elements", "QA audit: find elements without tags in a view", FindUntaggedElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("find_untagged_elements", args);
      });
      return toolResponse("find_untagged_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("find_untagged_elements", error, Date.now() - start);
    }
  });
}
