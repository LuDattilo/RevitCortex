import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowDataRoundtripInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerWorkflowDataRoundtripTool(server: McpServer): void {
  server.tool("workflow_data_roundtrip", "Export writable parameters to Excel for external editing, then re-import with import_from_excel", WorkflowDataRoundtripInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_data_roundtrip", args);
      });
      logToolCall({ tool: "workflow_data_roundtrip", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "workflow_data_roundtrip", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
