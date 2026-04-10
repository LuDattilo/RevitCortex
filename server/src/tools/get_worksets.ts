import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetWorksetsInput } from "../schemas/project.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetWorksetsTool(server: McpServer): void {
  server.tool(
    "get_worksets",
    "List worksets with open/close status and ownership. Only available for workshared documents.",
    GetWorksetsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_worksets", args);
        });
        return toolResponse("get_worksets", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_worksets", error, Date.now() - start);
      }
    }
  );
}
