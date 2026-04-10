import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AuditFamiliesInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAuditFamiliesTool(server: McpServer): void {
  server.tool("audit_families", "Family audit with health scores, unused detection, and instance counts", AuditFamiliesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("audit_families", args);
      });
      return toolResponse("audit_families", result, Date.now() - start, args);
    } catch (error) {
      return toolError("audit_families", error, Date.now() - start);
    }
  });
}
