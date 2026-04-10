import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowClashReviewInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerWorkflowClashReviewTool(server: McpServer): void {
  server.tool("workflow_clash_review", "Detect clashes between two categories and optionally create a 3D section box view", WorkflowClashReviewInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_clash_review", args);
      });
      logToolCall({ tool: "workflow_clash_review", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "workflow_clash_review", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
