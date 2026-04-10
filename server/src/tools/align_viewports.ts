import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AlignViewportsInput } from "../schemas/sheets.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAlignViewportsTool(server: McpServer): void {
  server.tool("align_viewports", "Align viewports across sheets by position", AlignViewportsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("align_viewports", args);
      });
      return toolResponse("align_viewports", result, Date.now() - start, args);
    } catch (error) {
      return toolError("align_viewports", error, Date.now() - start);
    }
  });
}
