import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowSheetSetInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerWorkflowSheetSetTool(server: McpServer): void {
  server.tool("workflow_sheet_set", "Auto-create sheets with title blocks from a definition list", WorkflowSheetSetInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_sheet_set", args);
      });
      return toolResponse("workflow_sheet_set", result, Date.now() - start, args);
    } catch (error) {
      return toolError("workflow_sheet_set", error, Date.now() - start);
    }
  });
}
