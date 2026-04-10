import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ModifyElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerModifyElementTool(server: McpServer): void {
  server.tool(
    "modify_element",
    "Move, rotate, mirror, or copy elements. Coordinates in mm.",
    ModifyElementInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("modify_element", args);
        });
        return toolResponse("modify_element", result, Date.now() - start, args);
      } catch (error) {
        return toolError("modify_element", error, Date.now() - start);
      }
    }
  );
}
