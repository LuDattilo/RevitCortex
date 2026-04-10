import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowSheetSetInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerWorkflowSheetSetTool(server: McpServer): void {
  server.tool("workflow_sheet_set", "Auto-create sheets with title blocks from a definition list", WorkflowSheetSetInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_sheet_set", args);
      });
      logToolCall({ tool: "workflow_sheet_set", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "workflow_sheet_set", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
