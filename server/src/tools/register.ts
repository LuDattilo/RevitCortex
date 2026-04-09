import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerSayHelloTool } from "./say_hello.js";
import { registerGetElementParametersTool } from "./get_element_parameters.js";
import { registerAIElementFilterTool } from "./ai_element_filter.js";
import { registerSetElementParametersTool } from "./set_element_parameters.js";
import { registerGetSelectedElementsTool } from "./get_selected_elements.js";
import { registerGetCurrentViewElementsTool } from "./get_current_view_elements.js";
import { registerGetLinkedElementsTool } from "./get_linked_elements.js";
import { registerGetElementsInSpatialVolumeTool } from "./get_elements_in_spatial_volume.js";
import { registerDeleteElementTool } from "./delete_element.js";
import { registerOperateElementTool } from "./operate_element.js";
import { registerChangeElementTypeTool } from "./change_element_type.js";
import { registerModifyElementTool } from "./modify_element.js";
import { logInfo } from "../logging/logger.js";

const toolRegistrations: Array<{ name: string; register: (s: McpServer) => void }> = [
  { name: "say_hello", register: registerSayHelloTool },
  { name: "get_element_parameters", register: registerGetElementParametersTool },
  { name: "ai_element_filter", register: registerAIElementFilterTool },
  { name: "set_element_parameters", register: registerSetElementParametersTool },
  { name: "get_selected_elements", register: registerGetSelectedElementsTool },
  { name: "get_current_view_elements", register: registerGetCurrentViewElementsTool },
  { name: "get_linked_elements", register: registerGetLinkedElementsTool },
  { name: "get_elements_in_spatial_volume", register: registerGetElementsInSpatialVolumeTool },
  { name: "delete_element", register: registerDeleteElementTool },
  { name: "operate_element", register: registerOperateElementTool },
  { name: "change_element_type", register: registerChangeElementTypeTool },
  { name: "modify_element", register: registerModifyElementTool },
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
