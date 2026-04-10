import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { OperateElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerOperateElementTool(server: McpServer): void {
  server.tool(
    "operate_element",
    "UI operations: select, color, hide, isolate, section box, transparency",
    OperateElementInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("operate_element", args);
        });
        return toolResponse("operate_element", result, Date.now() - start, args);
      } catch (error) {
        return toolError("operate_element", error, Date.now() - start);
      }
    }
  );
}
