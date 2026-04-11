import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { PinUnpinLinkInstanceInput } from "../schemas/linked-files.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerPinUnpinLinkInstanceTool(server: McpServer): void {
  server.tool("pin_unpin_link_instance", "Pins or unpins one or more linked file instances to prevent or allow accidental movement", PinUnpinLinkInstanceInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("pin_unpin_link_instance", args);
      });
      return toolResponse("pin_unpin_link_instance", result, Date.now() - start, args);
    } catch (error) {
      return toolError("pin_unpin_link_instance", error, Date.now() - start);
    }
  });
}
