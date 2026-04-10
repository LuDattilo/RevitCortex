import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetElementParametersInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetElementParametersTool(server: McpServer): void {
  server.tool(
    "get_element_parameters",
    "Get all parameters (instance and type) of one or more Revit elements by their IDs.",
    GetElementParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_element_parameters", args);
        });
        return toolResponse("get_element_parameters", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_element_parameters", error, Date.now() - start);
      }
    }
  );
}
