import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementParametersInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerSetElementParametersTool(server: McpServer): void {
  server.tool(
    "set_element_parameters",
    "Set parameter values on one or more Revit elements. Supports string, number, and boolean values.",
    SetElementParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("set_element_parameters", args);
        });
        return toolResponse("set_element_parameters", result, Date.now() - start, args);
      } catch (error) {
        return toolError("set_element_parameters", error, Date.now() - start);
      }
    }
  );
}
