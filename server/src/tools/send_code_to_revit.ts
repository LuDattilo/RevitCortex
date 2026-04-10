import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SendCodeToRevitInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSendCodeToRevitTool(server: McpServer): void {
  server.tool("send_code_to_revit", "Execute custom C# code in the Revit context", SendCodeToRevitInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("send_code_to_revit", args);
      });
      return toolResponse("send_code_to_revit", result, Date.now() - start, args);
    } catch (error) {
      return toolError("send_code_to_revit", error, Date.now() - start);
    }
  });
}
