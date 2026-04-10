const TOOL_CATEGORIES: Record<string, string> = {
  // Meta
  say_hello: "Meta",
  report_token_usage: "Meta",

  // Elements
  get_element_parameters: "Elements",
  ai_element_filter: "Elements",
  set_element_parameters: "Elements",
  get_selected_elements: "Elements",
  get_current_view_elements: "Elements",
  get_linked_elements: "Elements",
  get_elements_in_spatial_volume: "Elements",
  delete_element: "Elements",
  operate_element: "Elements",
  change_element_type: "Elements",
  modify_element: "Elements",
  copy_elements: "Elements",
  measure_between_elements: "Elements",
  renumber_elements: "Elements",
  find_untagged_elements: "Elements",
  find_undimensioned_elements: "Elements",
  export_elements_data: "Elements",
  match_element_properties: "Elements",
  set_element_phase: "Elements",
  set_element_workset: "Elements",
  color_elements: "Elements",
  filter_by_parameter_value: "Elements",
  save_selection: "Elements",
  load_selection: "Elements",
  delete_selection: "Elements",

  // Creation
  create_line_based_element: "Creation",
  create_point_based_element: "Creation",
  create_surface_based_element: "Creation",
  create_dimensions: "Creation",
  create_text_note: "Creation",
  create_color_legend: "Creation",
  create_floor: "Creation",
  create_grid: "Creation",
  create_level: "Creation",
  create_room: "Creation",
  create_array: "Creation",
  create_filled_region: "Creation",
  create_structural_framing_system: "Creation",

  // Views
  get_current_view_info: "Views",
  apply_view_template: "Views",
  batch_modify_view_range: "Views",
  create_view: "Views",
  duplicate_view: "Views",
  create_view_filter: "Views",
  override_graphics: "Views",
  section_box_from_selection: "Views",
  manage_unplaced_views: "Views",
  manage_view_templates: "Views",
  create_views_from_rooms: "Views",
  rename_views: "Views",
  lines_per_view_count: "Views",

  // Sheets
  create_sheet: "Sheets",
  place_viewport: "Sheets",
  align_viewports: "Sheets",
  batch_create_sheets: "Sheets",
  create_placeholder_sheets: "Sheets",
  duplicate_sheet_with_content: "Sheets",
  duplicate_sheet_with_views: "Sheets",

  // Schedules
  create_schedule: "Schedules",
  create_preset_schedule: "Schedules",
  get_schedule_data: "Schedules",
  delete_schedule: "Schedules",
  duplicate_schedule: "Schedules",
  modify_schedule: "Schedules",
  list_schedulable_fields: "Schedules",
  import_table: "Schedules",

  // Parameters
  add_shared_parameter: "Parameters",
  manage_project_parameters: "Parameters",
  add_prefix_suffix: "Parameters",
  get_shared_parameters: "Parameters",
  bulk_modify_parameter_values: "Parameters",
  clear_parameter_values: "Parameters",
  transfer_parameters: "Parameters",
  batch_rename: "Parameters",
  sync_csv_parameters: "Parameters",

  // Project
  get_project_info: "Project",
  get_phases: "Project",
  get_worksets: "Project",
  get_warnings: "Project",
  create_revision: "Project",
  manage_links: "Project",
  load_family: "Project",
  rename_families: "Project",
  get_available_family_types: "Project",
  list_family_sizes: "Project",
  get_room_openings: "Project",
  tag_rooms: "Project",
  tag_walls: "Project",
  duplicate_system_type: "Project",

  // Materials
  get_materials: "Materials",
  get_material_properties: "Materials",
  get_material_quantities: "Materials",
  set_material_properties: "Materials",
  create_material: "Materials",
  duplicate_material: "Materials",
  delete_material: "Materials",
  get_compound_structure: "Materials",
  set_compound_structure: "Materials",

  // Export
  export_room_data: "Export",
  export_schedule: "Export",
  export_families: "Export",
  export_shared_parameter_file: "Export",
  batch_export: "Export",
  export_to_excel: "Export",
  import_from_excel: "Export",

  // Audit
  analyze_model_statistics: "Audit",
  check_model_health: "Audit",
  audit_families: "Audit",
  purge_unused: "Audit",
  cad_link_cleanup: "Audit",
  clash_detection: "Audit",
  wipe_empty_tags: "Audit",

  // Workflows
  workflow_clash_review: "Workflows",
  workflow_room_documentation: "Workflows",
  workflow_sheet_set: "Workflows",
  workflow_model_audit: "Workflows",
  workflow_data_roundtrip: "Workflows",

  // Database
  store_project_data: "Database",
  store_room_data: "Database",
  query_stored_data: "Database",

  // Journal
  analyze_journal: "Journal",

  // Code
  send_code_to_revit: "Code",
};

export function getToolCategory(toolName: string): string {
  return TOOL_CATEGORIES[toolName] ?? "Other";
}
