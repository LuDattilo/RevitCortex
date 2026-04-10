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
import { registerCopyElementsTool } from "./copy_elements.js";
import { registerMeasureBetweenElementsTool } from "./measure_between_elements.js";
import { registerRenumberElementsTool } from "./renumber_elements.js";
import { registerFindUntaggedElementsTool } from "./find_untagged_elements.js";
import { registerFindUndimensionedElementsTool } from "./find_undimensioned_elements.js";
import { registerExportElementsDataTool } from "./export_elements_data.js";
import { registerMatchElementPropertiesTool } from "./match_element_properties.js";
import { registerCreateLineBasedElementTool } from "./create_line_based_element.js";
import { registerCreatePointBasedElementTool } from "./create_point_based_element.js";
import { registerCreateSurfaceBasedElementTool } from "./create_surface_based_element.js";
import { registerSetElementPhaseTool } from "./set_element_phase.js";
import { registerSetElementWorksetTool } from "./set_element_workset.js";
import { registerColorElementsTool } from "./color_elements.js";
import { registerGetProjectInfoTool } from "./get_project_info.js";
import { registerGetPhasesTool } from "./get_phases.js";
import { registerGetWorksetsTool } from "./get_worksets.js";
import { registerGetWarningsTool } from "./get_warnings.js";
import { registerGetCurrentViewInfoTool } from "./get_current_view_info.js";
import { registerFilterByParameterValueTool } from "./filter_by_parameter_value.js";
import { registerGetMaterialsTool } from "./get_materials.js";
import { registerGetMaterialPropertiesTool } from "./get_material_properties.js";
import { registerGetMaterialQuantitiesTool } from "./get_material_quantities.js";
import { registerGetScheduleDataTool } from "./get_schedule_data.js";
import { registerGetSharedParametersTool } from "./get_shared_parameters.js";
import { registerListSchedulableFieldsTool } from "./list_schedulable_fields.js";
import { registerGetAvailableFamilyTypesTool } from "./get_available_family_types.js";
import { registerListFamilySizesTool } from "./list_family_sizes.js";
import { registerLinesPerViewCountTool } from "./lines_per_view_count.js";
import { registerGetRoomOpeningsTool } from "./get_room_openings.js";
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
  { name: "copy_elements", register: registerCopyElementsTool },
  { name: "measure_between_elements", register: registerMeasureBetweenElementsTool },
  { name: "renumber_elements", register: registerRenumberElementsTool },
  { name: "find_untagged_elements", register: registerFindUntaggedElementsTool },
  { name: "find_undimensioned_elements", register: registerFindUndimensionedElementsTool },
  { name: "export_elements_data", register: registerExportElementsDataTool },
  { name: "match_element_properties", register: registerMatchElementPropertiesTool },
  { name: "create_line_based_element", register: registerCreateLineBasedElementTool },
  { name: "create_point_based_element", register: registerCreatePointBasedElementTool },
  { name: "create_surface_based_element", register: registerCreateSurfaceBasedElementTool },
  { name: "set_element_phase", register: registerSetElementPhaseTool },
  { name: "set_element_workset", register: registerSetElementWorksetTool },
  { name: "color_elements", register: registerColorElementsTool },
  { name: "get_project_info", register: registerGetProjectInfoTool },
  { name: "get_phases", register: registerGetPhasesTool },
  { name: "get_worksets", register: registerGetWorksetsTool },
  { name: "get_warnings", register: registerGetWarningsTool },
  { name: "get_current_view_info", register: registerGetCurrentViewInfoTool },
  { name: "filter_by_parameter_value", register: registerFilterByParameterValueTool },
  { name: "get_materials", register: registerGetMaterialsTool },
  { name: "get_material_properties", register: registerGetMaterialPropertiesTool },
  { name: "get_material_quantities", register: registerGetMaterialQuantitiesTool },
  { name: "get_schedule_data", register: registerGetScheduleDataTool },
  { name: "get_shared_parameters", register: registerGetSharedParametersTool },
  { name: "list_schedulable_fields", register: registerListSchedulableFieldsTool },
  { name: "get_available_family_types", register: registerGetAvailableFamilyTypesTool },
  { name: "list_family_sizes", register: registerListFamilySizesTool },
  { name: "lines_per_view_count", register: registerLinesPerViewCountTool },
  { name: "get_room_openings", register: registerGetRoomOpeningsTool },
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
