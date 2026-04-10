import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ImportTableInput } from "../schemas/annotations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerImportTableTool(server: McpServer): void {
  server.tool("import_table", "Import CSV/TSV file as a formatted table in a drafting or legend view", ImportTableInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("import_table", args);
      });
      return toolResponse("import_table", result, Date.now() - start, args);
    } catch (error) {
      return toolError("import_table", error, Date.now() - start);
    }
  });
}
