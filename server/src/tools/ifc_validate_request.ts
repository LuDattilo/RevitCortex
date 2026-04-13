import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { IfcValidateRequestInput } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerIfcValidateRequestTool(server: McpServer): void {
  server.tool(
    "ifc_validate_request",
    "Validate an IFC file path: check existence, format, size, and detect schema version from header.",
    IfcValidateRequestInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ifc_validate_request", args);
        });
        return toolResponse("ifc_validate_request", result, Date.now() - start, args);
      } catch (error) {
        return toolError("ifc_validate_request", error, Date.now() - start);
      }
    }
  );
}
