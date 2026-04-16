/**
 * RevitCortex Performance Benchmark
 * Tests key tools and identifies bottlenecks.
 */
import { Socket } from "net";

let reqId = 0;
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const client = new Socket();
    const id = String(++reqId);
    let buffer = "";
    const timeout = setTimeout(() => { client.destroy(); reject(new Error("TIMEOUT: " + method)); }, 120000);
    client.on("connect", () => { client.write(JSON.stringify({ jsonrpc: "2.0", method, params, id }) + "\n"); });
    client.on("data", (d) => {
      buffer += d.toString();
      const lines = buffer.split("\n");
      buffer = lines.pop() || "";
      for (const line of lines) {
        if (!line.trim()) continue;
        try { const p = JSON.parse(line); if (p.id === id) { clearTimeout(timeout); client.destroy(); resolve(p.error ? { ok: false, error: p.error } : { ok: true, result: p.result }); } } catch {}
      }
    });
    client.on("error", (e) => { clearTimeout(timeout); reject(e); });
    client.connect(8080, "localhost");
  });
}

const results = [];
async function bench(name, method, params) {
  const t0 = Date.now();
  try {
    const r = await send(method, params);
    const ms = Date.now() - t0;
    const flag = ms > 5000 ? "\u{1F534}" : ms > 1000 ? "\u{1F7E1}" : "\u{1F7E2}";
    const detail = r.ok ? "" : " \u2014 " + JSON.stringify(r.error).substring(0, 80);
    console.log(`${flag} ${String(ms).padStart(7)}ms ${r.ok ? "OK  " : "FAIL"} ${name}${detail}`);
    results.push({ name, ms, ok: r.ok });
    return { name, ms, ok: r.ok, result: r.result };
  } catch (e) {
    const ms = Date.now() - t0;
    console.log(`\u{1F534} ${String(ms).padStart(7)}ms CRASH ${name} \u2014 ${e.message}`);
    results.push({ name, ms, ok: false });
    return { name, ms, ok: false };
  }
}

async function run() {
  console.log("=== REVITCORTEX PERFORMANCE BENCHMARK ===");
  console.log("Flag: \u{1F7E2} <1s  \u{1F7E1} 1-5s  \u{1F534} >5s\n");

  // ── Read-only queries ──
  console.log("-- READ-ONLY QUERIES --");
  await bench("say_hello", "say_hello", {});
  await bench("get_project_info", "get_project_info", { includeLevels: true, includePhases: true });
  await bench("get_current_view_info", "get_current_view_info", {});
  await bench("get_phases", "get_phases", {});
  await bench("get_warnings (50)", "get_warnings", { maxWarnings: 50 });
  await bench("analyze_model_statistics", "analyze_model_statistics", { compact: true });
  await bench("check_model_health", "check_model_health", {});
  await bench("get_materials", "get_materials", {});
  await bench("get_shared_parameters", "get_shared_parameters", {});
  await bench("get_schedule_data (list)", "get_schedule_data", {});
  await bench("manage_project_units (get)", "manage_project_units", { action: "get" });
  await bench("manage_global_parameters (list)", "manage_global_parameters", { action: "list" });
  await bench("manage_additional_settings (line_styles)", "manage_additional_settings", { action: "list_line_styles" });
  await bench("manage_additional_settings (fill_patterns)", "manage_additional_settings", { action: "list_fill_patterns" });
  await bench("manage_additional_settings (line_patterns)", "manage_additional_settings", { action: "list_line_patterns" });
  await bench("manage_additional_settings (line_weights)", "manage_additional_settings", { action: "list_line_weights" });

  // ── Element queries ──
  console.log("\n-- ELEMENT QUERIES --");
  const cats = ["OST_Walls", "OST_Doors", "OST_Windows", "OST_Rooms", "OST_Floors", "OST_Sheets"];
  const ids = {};
  for (const cat of cats) {
    const r = await bench("ai_element_filter " + cat, "ai_element_filter", { data: { filterCategory: cat, includeInstances: true, maxElements: 10 } });
    if (r.ok) ids[cat] = (r.result?.elements || []).map(e => e.elementId).filter(Boolean);
  }
  await bench("get_selected_elements", "get_selected_elements", { limit: 10 });
  await bench("get_current_view_elements", "get_current_view_elements", { modelCategoryList: ["OST_Walls"], limit: 20 });

  // ── Parameter operations ──
  console.log("\n-- PARAMETER OPERATIONS --");
  if (ids.OST_Walls?.length) {
    await bench("get_element_params (1 wall)", "get_element_parameters", { elementIds: [ids.OST_Walls[0]], includeTypeParameters: true });
    await bench("get_element_params (10 walls)", "get_element_parameters", { elementIds: ids.OST_Walls.slice(0, 10), includeTypeParameters: true });
  }
  if (ids.OST_Sheets?.length) {
    await bench("get_element_params (10 sheets)", "get_element_parameters", { elementIds: ids.OST_Sheets.slice(0, 10), includeTypeParameters: true });
  }

  // ── Export operations ──
  console.log("\n-- EXPORT OPERATIONS --");
  await bench("export_data (doors, specific params)", "export_elements_data", { categories: ["OST_Doors"], parameterNames: ["Mark", "Width", "Height"], maxElements: 50 });
  await bench("export_data (doors, all params)", "export_elements_data", { categories: ["OST_Doors"], maxElements: 50 });
  await bench("export_data (doors, +typeParams)", "export_elements_data", { categories: ["OST_Doors"], includeTypeParameters: true, maxElements: 50 });
  await bench("export_data (sheets, all)", "export_elements_data", { categories: ["OST_Sheets"], maxElements: 100 });
  await bench("export_data (sheets, +typeParams)", "export_elements_data", { categories: ["OST_Sheets"], includeTypeParameters: true, maxElements: 100 });
  await bench("export_data (walls, 100)", "export_elements_data", { categories: ["OST_Walls"], maxElements: 100 });
  await bench("export_data (walls, +typeParams)", "export_elements_data", { categories: ["OST_Walls"], includeTypeParameters: true, maxElements: 100 });
  await bench("export_room_data", "export_room_data", { maxResults: 200 });

  // ── Filter operations ──
  console.log("\n-- FILTER OPERATIONS --");
  await bench("filter_by_param (walls, Area>0)", "filter_by_parameter_value", { categories: ["OST_Walls"], parameterName: "Area", condition: "greater_than", value: "0" });
  await bench("filter_by_param (doors, Mark not empty)", "filter_by_parameter_value", { categories: ["OST_Doors"], parameterName: "Mark", condition: "is_not_empty" });
  await bench("filter_by_param (rooms, Name not empty)", "filter_by_parameter_value", { categories: ["OST_Rooms"], parameterName: "Name", condition: "is_not_empty" });

  // ── Schedules & families ──
  console.log("\n-- SCHEDULES & FAMILIES --");
  await bench("get_schedule_data (Sheet Index)", "get_schedule_data", { scheduleId: 2182900 });
  await bench("get_available_family_types", "get_available_family_types", { limit: 50 });
  await bench("list_family_sizes", "list_family_sizes", { limit: 20 });
  await bench("audit_families", "audit_families", { includeUnused: true });

  // ── Materials ──
  console.log("\n-- MATERIALS --");
  await bench("get_material_quantities (walls)", "get_material_quantities", { categoryFilters: ["OST_Walls"], maxResults: 10 });
  if (ids.OST_Walls?.[0]) {
    await bench("get_compound_structure", "get_compound_structure", { elementId: ids.OST_Walls[0] });
  }

  // ── Rooms ──
  console.log("\n-- ROOMS --");
  await bench("get_room_openings (all)", "get_room_openings", { elementType: "both" });

  // ── Views & Links ──
  console.log("\n-- VIEWS & LINKS --");
  await bench("manage_links", "manage_links", { action: "list" });
  await bench("manage_unplaced_views", "manage_unplaced_views", { action: "list", maxResults: 50 });
  await bench("manage_view_templates", "manage_view_templates", { action: "list" });
  await bench("lines_per_view_count", "lines_per_view_count", { limit: 20 });

  // ── Workflows ──
  console.log("\n-- WORKFLOWS --");
  await bench("workflow_model_audit", "workflow_model_audit", { includeWarnings: true, includeFamilies: true, maxWarnings: 20 });

  // ── Summary ──
  console.log("\n=== SUMMARY ===");
  const slow = results.filter(r => r.ms > 1000).sort((a, b) => b.ms - a.ms);
  const totalMs = results.reduce((s, r) => s + r.ms, 0);
  const failCount = results.filter(r => !r.ok).length;
  console.log(`Total: ${results.length} tests, ${failCount} failures, ${(totalMs / 1000).toFixed(1)}s total`);
  if (slow.length > 0) {
    console.log(`\nSLOW (>1s):`);
    for (const s of slow) {
      console.log(`  ${s.ms > 5000 ? "\u{1F534}" : "\u{1F7E1}"} ${(s.ms / 1000).toFixed(1)}s ${s.name}`);
    }
  }
}

run().then(() => process.exit(0)).catch(e => { console.error("FATAL:", e); process.exit(1); });
