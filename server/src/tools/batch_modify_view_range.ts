import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchModifyViewRangeInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerBatchModifyViewRangeTool(server: McpServer): void {
  server.tool("batch_modify_view_range", "Modify view range offsets for multiple views", BatchModifyViewRangeInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_modify_view_range", args);
      });
      return toolResponse("batch_modify_view_range", result, Date.now() - start, args);
    } catch (error) {
      return toolError("batch_modify_view_range", error, Date.now() - start);
    }
  });
}
