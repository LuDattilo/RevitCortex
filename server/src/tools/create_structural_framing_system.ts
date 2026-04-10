import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateStructuralFramingSystemInput } from "../schemas/schedules.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateStructuralFramingSystemTool(server: McpServer): void {
  server.tool("create_structural_framing_system", "Create a beam system on a level with spacing", CreateStructuralFramingSystemInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_structural_framing_system", args);
      });
      return toolResponse("create_structural_framing_system", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_structural_framing_system", error, Date.now() - start);
    }
  });
}
