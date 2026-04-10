import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WorkflowRoomDocumentationInput } from "../schemas/excel-workflows.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerWorkflowRoomDocumentationTool(server: McpServer): void {
  server.tool("workflow_room_documentation", "Auto-generate callout views and sections from rooms on a level", WorkflowRoomDocumentationInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("workflow_room_documentation", args);
      });
      return toolResponse("workflow_room_documentation", result, Date.now() - start, args);
    } catch (error) {
      return toolError("workflow_room_documentation", error, Date.now() - start);
    }
  });
}
