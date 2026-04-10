import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowClashReviewInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerWorkflowClashReviewTool(server: McpServer): void {
  server.tool("workflow_clash_review", "Detect clashes between two categories and optionally create a 3D section box view", WorkflowClashReviewInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_clash_review", args);
      });
      return toolResponse("workflow_clash_review", result, Date.now() - start, args);
    } catch (error) {
      return toolError("workflow_clash_review", error, Date.now() - start);
    }
  });
}
