import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateSheetInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateSheetTool(server: McpServer): void {
  server.tool("create_sheet", "Create a new sheet with title block", CreateSheetInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_sheet", args);
      });
      return toolResponse("create_sheet", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_sheet", error, Date.now() - start);
    }
  });
}
