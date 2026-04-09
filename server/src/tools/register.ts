import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerSayHelloTool } from "./say_hello.js";
import { registerGetElementParametersTool } from "./get_element_parameters.js";
import { registerAIElementFilterTool } from "./ai_element_filter.js";
import { registerSetElementParametersTool } from "./set_element_parameters.js";
import { logInfo } from "../logging/logger.js";

const toolRegistrations: Array<{ name: string; register: (s: McpServer) => void }> = [
  { name: "say_hello", register: registerSayHelloTool },
  { name: "get_element_parameters", register: registerGetElementParametersTool },
  { name: "ai_element_filter", register: registerAIElementFilterTool },
  { name: "set_element_parameters", register: registerSetElementParametersTool },
];

export function registerTools(server: McpServer): void {
  for (const { name, register } of toolRegistrations) {
    try {
      register(server);
      logInfo(`Registered tool: ${name}`);
    } catch (error) {
      logInfo(`Failed to register tool ${name}: ${error}`);
    }
  }
  logInfo(`Total tools registered: ${toolRegistrations.length}`);
}
