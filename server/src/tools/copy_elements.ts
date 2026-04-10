import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CopyElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCopyElementsTool(server: McpServer): void {
  server.tool("copy_elements", "Copy elements with optional offset and view-to-view support", CopyElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("copy_elements", args);
      });
      return toolResponse("copy_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("copy_elements", error, Date.now() - start);
    }
  });
}
