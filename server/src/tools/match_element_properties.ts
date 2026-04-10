import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { MatchElementPropertiesInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerMatchElementPropertiesTool(server: McpServer): void {
  server.tool("match_element_properties", "Copy parameter values from source to target elements", MatchElementPropertiesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("match_element_properties", args);
      });
      return toolResponse("match_element_properties", result, Date.now() - start, args);
    } catch (error) {
      return toolError("match_element_properties", error, Date.now() - start);
    }
  });
}
