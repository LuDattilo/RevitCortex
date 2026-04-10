import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { MeasureBetweenElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerMeasureBetweenElementsTool(server: McpServer): void {
  server.tool("measure_between_elements", "Measure distance between elements or points in mm", MeasureBetweenElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("measure_between_elements", args);
      });
      return toolResponse("measure_between_elements", result, Date.now() - start, args);
    } catch (error) {
      return toolError("measure_between_elements", error, Date.now() - start);
    }
  });
}
