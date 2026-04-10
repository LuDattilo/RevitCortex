import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementPhaseInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSetElementPhaseTool(server: McpServer): void {
  server.tool("set_element_phase", "Assign created/demolished phase to elements", SetElementPhaseInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("set_element_phase", args);
      });
      return toolResponse("set_element_phase", result, Date.now() - start, args);
    } catch (error) {
      return toolError("set_element_phase", error, Date.now() - start);
    }
  });
}
