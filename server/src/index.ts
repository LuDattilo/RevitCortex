import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { registerTools } from "./tools/register.js";
import { getDatabase } from "./database/db.js";
import { getUsageDatabase } from "./database/usageDb.js";
import { logInfo, logError } from "./logging/logger.js";

const server = new McpServer({
  name: "revitcortex-server",
  version: "0.1.0",
});

async function main(): Promise<void> {
  await getDatabase();
  await getUsageDatabase();
  logInfo("Database initialized");
  registerTools(server);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  logInfo("RevitCortex MCP Server started successfully");
}

main().catch((error) => {
  logError("Failed to start RevitCortex server", error);
  process.exit(1);
});
