import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowModelAuditInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerWorkflowModelAuditTool(server: McpServer): void {
  server.tool("workflow_model_audit", "Comprehensive model audit: health score, warnings, family analysis, and recommendations", WorkflowModelAuditInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_model_audit", args);
      });
      return toolResponse("workflow_model_audit", result, Date.now() - start, args);
    } catch (error) {
      return toolError("workflow_model_audit", error, Date.now() - start);
    }
  });
}
