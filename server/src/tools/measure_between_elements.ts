import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { MeasureBetweenElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerMeasureBetweenElementsTool(server: McpServer): void {
  server.tool("measure_between_elements", "Measure distance between elements or points in mm", MeasureBetweenElementsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("measure_between_elements", args);
      });
      logToolCall({ tool: "measure_between_elements", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "measure_between_elements", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
