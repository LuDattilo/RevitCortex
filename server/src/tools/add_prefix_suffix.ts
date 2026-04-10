import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AddPrefixSuffixInput } from "../schemas/parameters.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAddPrefixSuffixTool(server: McpServer): void {
  server.tool("add_prefix_suffix", "Add prefix/suffix to parameter values with dry-run preview", AddPrefixSuffixInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("add_prefix_suffix", args);
      });
      return toolResponse("add_prefix_suffix", result, Date.now() - start, args);
    } catch (error) {
      return toolError("add_prefix_suffix", error, Date.now() - start);
    }
  });
}
