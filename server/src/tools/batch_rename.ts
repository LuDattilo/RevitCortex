import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { BatchRenameInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerBatchRenameTool(server: McpServer): void {
  server.tool("batch_rename", "Batch rename views, sheets, levels, grids, or rooms", BatchRenameInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("batch_rename", args);
      });
      return toolResponse("batch_rename", result, Date.now() - start, args);
    } catch (error) {
      return toolError("batch_rename", error, Date.now() - start);
    }
  });
}
