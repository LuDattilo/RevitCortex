import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ClashDetectionInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerClashDetectionTool(server: McpServer): void {
  server.tool("clash_detection", "Detect geometric intersections between element sets", ClashDetectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("clash_detection", args);
      });
      return toolResponse("clash_detection", result, Date.now() - start, args);
    } catch (error) {
      return toolError("clash_detection", error, Date.now() - start);
    }
  });
}
