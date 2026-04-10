import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowModelAuditInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerWorkflowModelAuditTool(server: McpServer): void {
  server.tool("workflow_model_audit", "Comprehensive model audit: health score, warnings, family analysis, and recommendations", WorkflowModelAuditInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_model_audit", args);
      });
      logToolCall({ tool: "workflow_model_audit", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "workflow_model_audit", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
