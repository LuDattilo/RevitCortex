import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { FindUndimensionedElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerFindUndimensionedElementsTool(server: McpServer): void {
  server.tool("find_undimensioned_elements", "QA audit: find elements not referenced by dimensions", FindUndimensionedElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("find_undimensioned_elements", args);
      });
      return toolResponse("find_undimensioned_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("find_undimensioned_elements", error, Date.now() - start);
    }
  });
}
