# Typed MCP signatures for remaining tools

Date: 2026-04-20
Status: proposed — awaiting scheduling

## Problem

81 of 152 MCP tools in `src/RevitCortex.Server/Tools/*.cs` are still registered
with a single opaque `[Description("JSON parameters")] string data` argument.
On the MCP wire this produces `inputSchema = { data: string }`, so both
`tool-schemas.txt` and any LLM browsing the tool list see the tool as
`toolname(data:string!)` and cannot discover its real parameters, defaults,
or enums without reading the C# sources.

Impact:

- LLMs waste tokens guessing the payload shape and hit `InvalidInput`
  errors on the first call.
- `tool-schemas.txt` (compact one-line-per-tool file designed precisely to
  give the LLM the full contract cheaply) is useless for these 81 tools.
- The pattern has already diverged: ~71 tools use typed signatures, the
  rest don't. Mixed discoverability confuses users and LLMs alike.

Reference precedent: `manage_project_parameters` was converted on 2026-04-20
in the same PR that added `categoriesMode`
(`src/RevitCortex.Server/Tools/ParameterTools.cs:127`). The pattern is
straightforward.

## Goal

Convert all 81 remaining tools to typed MCP signatures. Each tool's
`inputSchema` must reflect the real parameters of the underlying
`ICortexTool.Execute` implementation in `RevitCortex.Tools`.

Out of scope:

- Changing the JSON-RPC payload sent to the plugin — the C# tool keeps
  parsing `JObject input`, the server just builds that JObject from typed
  arguments instead of `JObject.Parse(data)`.
- Rewriting the plugin-side tools.

## The conversion pattern

**Before** (opaque):

```csharp
[McpServerTool(Name = "foo"), Description("...")]
public static async Task<string> Foo(
    RevitConnectionManager revit,
    [Description("JSON parameters")] string data,
    CancellationToken ct = default)
{
    var result = await revit.ExecuteAsync("foo", JObject.Parse(data), ct);
    return result.ToString();
}
```

**After** (typed):

```csharp
[McpServerTool(Name = "foo"), Description("...")]
public static async Task<string> Foo(
    RevitConnectionManager revit,
    [Description("what this arg does, with enum values if any")] string action,
    [Description("...")] string? name = null,
    [Description("...")] int? count = null,
    CancellationToken ct = default)
{
    var p = new JObject { ["action"] = action };
    if (name  != null) p["name"]  = name;
    if (count != null) p["count"] = count;
    var result = await revit.ExecuteAsync("foo", p, ct);
    return result.ToString();
}
```

Rules:

- Use nullable types (`string?`, `int?`, `bool?`, `double?`, `string[]?`)
  for optional arguments. Required arguments are non-nullable with no
  default.
- `string[]` becomes a `JArray` via `new JArray(myArray)`.
- Keep parameter names identical to the JSON keys the plugin-side tool
  expects — no rename.
- For enum-like strings, list the accepted values in the `[Description]`.
- Do NOT introduce a typed DTO class per tool — the MCP SDK generates the
  schema from the method signature directly, and a DTO adds one layer of
  indirection for no benefit. See `ProjectTools.cs:388` (`ManageProjectUnits`)
  as the canonical example.

## How to discover the real parameter shape

Each MCP tool delegates to an `ICortexTool` implementation in
`src/RevitCortex.Tools/<Category>/<Name>Tool.cs`. The arguments consumed
by `Execute(JObject input, ...)` ARE the MCP parameters. Typical lookup
patterns:

- `input["action"]?.Value<string>()` → `string action`
- `input["maxElements"]?.Value<int>()` → `int? maxElements`
- `input["categories"]?.ToObject<List<string>>()` → `string[]? categories`

Read every `input[...]` access in the plugin-side tool and mirror it in the
server signature. Do not guess.

## List of 81 tools to convert

Grouped by server file for convenient PR-per-file splitting.

**ParameterTools.cs** — add_shared_parameter, get_shared_parameters,
match_element_properties, sync_csv_parameters, transfer_parameters

**ElementTools.cs** (or equivalents) — align_link_to_host, copy_elements,
create_array, create_dimensions, create_filled_region,
create_line_based_element, create_point_based_element,
create_structural_framing_system, create_surface_based_element,
create_text_note, delete_selection, duplicate_family_type, duplicate_material,
export_elements_data, export_families, get_elements_in_spatial_volume,
get_linked_elements, get_material_properties, get_material_quantities,
get_room_openings, highlight_linked_element, list_family_sizes,
list_schedulable_fields, modify_element, move_link_instance, override_graphics,
pin_unpin_link_instance, renumber_elements, set_element_phase,
set_element_workset, delete_material, set_material_properties,
rename_families, tag_rooms, tag_walls, measure_between_elements

**ViewTools.cs** — align_viewports, apply_view_template,
batch_modify_view_range, create_color_legend, create_placeholder_sheets,
create_revision, create_views_from_rooms, duplicate_sheet_with_views,
lines_per_view_count, manage_view_templates, modify_schedule

**LinkTools.cs** — add_linked_file, cad_link_cleanup,
reload_linked_file_from

**IfcTools.cs** — ifc_analyze_rebuildability,
ifc_compare_original_vs_rebuilt, ifc_export_basic,
ifc_export_with_configuration, ifc_get_export_configuration, ifc_link,
ifc_list_rebuild_candidates, ifc_open_or_import,
ifc_rebuild_family_instances, ifc_rebuild_floors, ifc_rebuild_openings,
ifc_rebuild_roofs, ifc_rebuild_structural_members, ifc_rebuild_walls,
ifc_reload_link, ifc_set_family_mapping_file,
ifc_tag_unreconstructable_elements, ifc_validate_request

**WorkflowTools.cs** — workflow_clash_review, workflow_data_roundtrip,
workflow_room_documentation, workflow_sheet_set

**BatchTools.cs** — batch_create_sheets, batch_export, import_from_excel,
import_table

(Verify these file groupings when starting — the server file names may
differ. The source of truth is which C# file contains the tool's
`[McpServerTool(Name = "<name>")]` attribute.)

## Suggested execution strategy

Split into ~6-8 PRs, one per server file, because:

- Each PR is independently testable with `dotnet build` + one
  `node server/generate-tool-schemas-csharp.mjs` run.
- A bug in one file's conversion doesn't block the others.
- Review stays tractable (~10-15 tool conversions per PR).

For each PR:

1. Read every plugin-side `*Tool.cs` referenced to extract the exact
   `input[...]` accesses.
2. Rewrite the server-side method signature.
3. Build `RevitCortex.Server`.
4. Regenerate `tool-schemas.txt` — verify the converted tools no longer
   show `(data:string!)`.
5. Build `Debug R25` and `Debug R24` for `RevitCortex.Tools` to confirm
   no regression on the plugin side (we are not changing plugin code, but
   the safety net is cheap).
6. Update `USER_GUIDE.md` entries if the tool's description deserves a
   richer description now that parameters are visible.

## Non-goals / explicit constraints

- Do NOT change the JSON contract the plugin-side tool expects. The server
  is a thin adapter; renaming `maxElements` → `maxItems` is a breaking
  change for existing client code.
- Do NOT merge this work with behavioral changes to the tools. Typed
  signature conversions should be pure refactors.
- Do NOT introduce typed DTO records just because they look cleaner.
  The MCP SDK generates the schema from method parameters; an extra
  record layer buys nothing.

## Definition of done

- `grep -c "(data:string!)$" tool-schemas.txt` returns 0.
- Every tool's signature in `tool-schemas.txt` lists at least one named
  parameter other than `data`.
- `Debug R25`, `Debug R24`, and `RevitCortex.Server` all build green.
- `USER_GUIDE.md` updated for any tool whose description materially
  improves now that parameters are visible.
