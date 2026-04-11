import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AlignLinkToHostInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAlignLinkToHostTool(server: McpServer): void {
  server.tool("align_link_to_host", "Aligns a link instance to the host project's internal origin or shared coordinates", AlignLinkToHostInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("align_link_to_host", args);
      });
      return toolResponse("align_link_to_host", result, Date.now() - start, args);
    } catch (error) {
      return toolError("align_link_to_host", error, Date.now() - start);
    }
  });
}
