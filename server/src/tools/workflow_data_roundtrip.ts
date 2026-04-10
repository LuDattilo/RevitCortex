import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowDataRoundtripInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerWorkflowDataRoundtripTool(server: McpServer): void {
  server.tool("workflow_data_roundtrip", "Export writable parameters to Excel for external editing, then re-import with import_from_excel", WorkflowDataRoundtripInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_data_roundtrip", args);
      });
      return toolResponse("workflow_data_roundtrip", result, Date.now() - start, args);
    } catch (error) {
      return toolError("workflow_data_roundtrip", error, Date.now() - start);
    }
  });
}
