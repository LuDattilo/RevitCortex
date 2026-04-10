import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetPhasesInput } from "../schemas/project.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetPhasesTool(server: McpServer): void {
  server.tool(
    "get_phases",
    "List all phases in the project with their sequence order and optionally phase filters.",
    GetPhasesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_phases", args);
        });
        return toolResponse("get_phases", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_phases", error, Date.now() - start);
      }
    }
  );
}
