import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowRoomDocumentationInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerWorkflowRoomDocumentationTool(server: McpServer): void {
  server.tool("workflow_room_documentation", "Auto-generate callout views and sections from rooms on a level", WorkflowRoomDocumentationInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_room_documentation", args);
      });
      logToolCall({ tool: "workflow_room_documentation", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "workflow_room_documentation", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
