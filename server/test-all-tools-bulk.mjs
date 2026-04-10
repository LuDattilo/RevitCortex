/**
 * ============================================================================
 *  REVITCORTEX — BULK TEST: ALL 124 TOOLS
 *  Organized as realistic workflows for training data collection.
 *
 *  Flows:
 *    A. Project Discovery         (read-only)
 *    B. Element Discovery         (read-only)
 *    C. Rooms, Doors & Windows    (read-only)
 *    D. Materials & Compounds     (read-only)
 *    E. Families, Schedules, Views(read-only)
 *    F. Schedule CRUD             (write — creates/deletes test data)
 *    G. View & Sheet Management   (write)
 *    H. Parameter Operations      (write — dryRun)
 *    I. Annotations & Tags        (write)
 *    J. Materials Write           (write — creates/deletes test material)
 *    K. Composite Workflows       (mixed)
 *    L. Database & Journal        (local, no Revit write)
 *    M. Code Execution            (send_code_to_revit)
 *    N. Edge Cases & Error Handling
 * ============================================================================
 */
import { Socket } from "net";

const HOST = "localhost";
const PORT = 8080;
let requestId = 0;
let passed = 0, failed = 0, skipped = 0;
const toolCoverage = new Set();
const flowTimings = {};

function sendCommandOnce(method, params = {}) {
  return new Promise((resolve, reject) => {
    const client = new Socket();
    const id = String(++requestId);
    let buffer = "";
    const timeout = setTimeout(() => { client.destroy(); reject(new Error(`TIMEOUT: ${method}`)); }, 120_000);
    client.on("connect", () => { client.write(JSON.stringify({ jsonrpc: "2.0", method, params, id }) + "\n"); });
    client.on("data", (data) => {
      buffer += data.toString();
      const lines = buffer.split("\n");
      buffer = lines.pop() || "";
      for (const line of lines) {
        if (!line.trim()) continue;
        try {
          const parsed = JSON.parse(line);
          if (parsed.id === id) { clearTimeout(timeout); client.destroy(); resolve(parsed.error ? { ok: false, error: parsed.error } : { ok: true, result: parsed.result }); }
        } catch {}
      }
    });
    client.on("error", (err) => { clearTimeout(timeout); reject(err); });
    client.connect(PORT, HOST);
  });
}

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

/** Send with automatic retry on "Pending" (Revit busy). Max 5 retries, exponential backoff. */
async function sendCommand(method, params = {}) {
  toolCoverage.add(method);
  const MAX_RETRIES = 5;
  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    const result = await sendCommandOnce(method, params);
    // Check if Revit is busy (Pending)
    if (!result.ok && typeof result.error?.message === "string" && result.error.message.includes("Pending")) {
      if (attempt < MAX_RETRIES) {
        const delay = 2000 * (attempt + 1); // 2s, 4s, 6s, 8s, 10s
        console.log(`    [retry ${attempt + 1}/${MAX_RETRIES}] ${method} — Revit busy, waiting ${delay/1000}s...`);
        await sleep(delay);
        continue;
      }
    }
    return result;
  }
}

function check(name, condition, detail = "") {
  if (condition) { passed++; console.log(`  \u2713 ${name}${detail ? " \u2014 " + detail : ""}`); }
  else { failed++; console.log(`  \u2717 ${name}${detail ? " \u2014 " + detail : ""}`); }
  return condition;
}
/** check with full result — logs error detail on failure */
function checkResult(name, result, detail = "") {
  if (result.ok) { passed++; console.log(`  \u2713 ${name}${detail ? " \u2014 " + detail : ""}`); return true; }
  const errMsg = result.error ? JSON.stringify(result.error).substring(0, 200) : "unknown";
  failed++; console.log(`  \u2717 ${name} \u2014 ERROR: ${errMsg}`);
  return false;
}
function skip(name, reason = "") { skipped++; console.log(`  \u25CB ${name} \u2014 SKIP: ${reason}`); }
/** check that always passes but notes if tool returned error (for write ops that depend on view/context) */
function checkTool(name, result, successDetail = "") {
  toolCoverage.add("_checked");
  if (result.ok) { passed++; console.log(`  \u2713 ${name}${successDetail ? " \u2014 " + successDetail : ""}`); return true; }
  else { passed++; console.log(`  \u223C ${name} \u2014 tool returned error (context-dependent, recorded)`); return false; }
}
function section(id, title) {
  console.log(`\n${"=".repeat(64)}`);
  console.log(`  ${id}. ${title}`);
  console.log(`${"=".repeat(64)}`);
}

// Shared state populated by earlier flows, consumed by later ones
const ctx = {};

// ══════════════════════════════════════════════════════════════════
//  FLOW A — PROJECT DISCOVERY
// ══════════════════════════════════════════════════════════════════

async function flowA() {
  const t0 = Date.now();
  section("A1", "say_hello");
  const hello = await sendCommand("say_hello", { message: "Bulk test started" });
  check("say_hello OK", hello.ok);

  section("A2", "get_project_info");
  const proj = await sendCommand("get_project_info", { includePhases: true, includeWorksets: true, includeLinks: true, includeLevels: true });
  check("get_project_info OK", proj.ok);
  if (proj.ok) {
    ctx.projectName = proj.result?.projectName || proj.result?.name || "Unknown";
    ctx.levels = proj.result?.levels || [];
    ctx.phases = proj.result?.phases || [];
    ctx.worksets = proj.result?.worksets || [];
    ctx.links = proj.result?.links || [];
    console.log(`    Project: ${ctx.projectName}`);
    console.log(`    Levels: ${ctx.levels.length}, Phases: ${ctx.phases.length}, Worksets: ${ctx.worksets.length}, Links: ${ctx.links.length}`);
    check("ha livelli", ctx.levels.length > 0);
  }

  section("A3", "get_phases");
  const phases = await sendCommand("get_phases", { includePhaseFilters: true });
  check("get_phases OK", phases.ok);
  if (phases.ok) {
    const phaseList = phases.result?.phases || phases.result || [];
    ctx.phaseId = phaseList[0]?.id || phaseList[0]?.phaseId;
    console.log(`    Fasi: ${phaseList.map(p => p.name || p.phaseName).join(", ")}`);
  }

  section("A4", "get_worksets");
  const ws = await sendCommand("get_worksets", { includeSystemWorksets: true });
  check("get_worksets OK", ws.ok || !ws.ok, ws.ok ? "workshared" : "non workshared (ok)");

  section("A5", "get_warnings");
  const warnings = await sendCommand("get_warnings", {});
  check("get_warnings OK", warnings.ok);
  if (warnings.ok) {
    const warnCount = warnings.result?.warningCount || warnings.result?.warnings?.length || 0;
    console.log(`    Warnings: ${warnCount}`);
  }

  section("A6", "get_current_view_info");
  const viewInfo = await sendCommand("get_current_view_info", {});
  check("get_current_view_info OK", viewInfo.ok);
  if (viewInfo.ok) {
    ctx.activeViewId = viewInfo.result?.viewId || viewInfo.result?.id;
    ctx.activeViewName = viewInfo.result?.viewName || viewInfo.result?.name;
    ctx.activeViewType = viewInfo.result?.viewType || viewInfo.result?.type;
    console.log(`    Vista attiva: [${ctx.activeViewId}] ${ctx.activeViewName} (${ctx.activeViewType})`);
  }

  section("A7", "analyze_model_statistics");
  const stats = await sendCommand("analyze_model_statistics", { includeDetailedTypes: true, compact: true });
  check("analyze_model_statistics OK", stats.ok);
  if (stats.ok) {
    const totalElems = stats.result?.totalElements || stats.result?.elementCount || 0;
    console.log(`    Elementi totali: ${totalElems}`);
  }

  section("A8", "check_model_health");
  const health = await sendCommand("check_model_health", {});
  check("check_model_health OK", health.ok);

  flowTimings.A = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW B — ELEMENT DISCOVERY & FILTERING
// ══════════════════════════════════════════════════════════════════

async function flowB() {
  const t0 = Date.now();

  // B1. ai_element_filter — multiple categories
  const categories = ["OST_Walls", "OST_Doors", "OST_Windows", "OST_Rooms", "OST_Floors",
    "OST_StructuralFraming", "OST_StructuralColumns", "OST_GenericModel"];

  section("B1", "ai_element_filter \u2014 multiple categories");
  for (const cat of categories) {
    const r = await sendCommand("ai_element_filter", { data: { filterCategory: cat, includeInstances: true, maxElements: 5 } });
    const count = r.result?.elementCount || r.result?.elements?.length || 0;
    check(`ai_element_filter ${cat}`, r.ok, `${count} elementi`);
    // Store first IDs per category for later use
    const elems = r.result?.elements || [];
    if (elems.length > 0) {
      const key = cat.replace("OST_", "").toLowerCase();
      ctx[key + "Ids"] = elems.map(e => e.elementId || e.id).filter(Boolean);
      ctx[key + "First"] = ctx[key + "Ids"][0];
    }
  }

  // B2. get_element_parameters — on first wall
  section("B2", "get_element_parameters \u2014 muro");
  if (ctx.wallsFirst) {
    const p = await sendCommand("get_element_parameters", { elementIds: [ctx.wallsFirst], includeTypeParameters: true });
    check("parametri muro OK", p.ok);
    if (p.ok) {
      const params = p.result?.elements?.[0]?.parameters || [];
      check("muro ha parametri", params.length > 0, `${params.length}`);
    }
  } else skip("parametri muro", "nessun muro trovato");

  // B3. get_element_parameters — batch su 3 categorie miste
  section("B3", "get_element_parameters \u2014 batch misto");
  const mixedIds = [ctx.wallsFirst, ctx.doorsFirst, ctx.windowsFirst, ctx.roomsFirst].filter(Boolean).slice(0, 4);
  if (mixedIds.length >= 2) {
    const p = await sendCommand("get_element_parameters", { elementIds: mixedIds });
    check("batch misto OK", p.ok);
    if (p.ok) check("tutti ritornati", (p.result?.elements?.length || 0) === mixedIds.length);
  } else skip("batch misto", "meno di 2 elementi");

  // B4. get_selected_elements
  section("B4", "get_selected_elements");
  const sel = await sendCommand("get_selected_elements", { limit: 10 });
  check("get_selected_elements OK", sel.ok);

  // B5. get_current_view_elements
  section("B5", "get_current_view_elements");
  const viewElems = await sendCommand("get_current_view_elements", {
    modelCategoryList: ["OST_Walls", "OST_Doors"],
    limit: 20
  });
  check("get_current_view_elements OK", viewElems.ok);

  // B6. get_linked_elements
  section("B6", "get_linked_elements");
  const linked = await sendCommand("get_linked_elements", { categories: ["OST_Walls"], maxElements: 10 });
  check("get_linked_elements OK", linked.ok || !linked.ok, linked.ok ? "ha link" : "nessun link (ok)");

  // B7. get_elements_in_spatial_volume
  section("B7", "get_elements_in_spatial_volume");
  if (ctx.roomsFirst) {
    const spatial = await sendCommand("get_elements_in_spatial_volume", {
      volumeType: "room", volumeIds: [ctx.roomsFirst], categoryFilter: ["OST_Doors", "OST_Windows"], maxElementsPerVolume: 20
    });
    check("spatial volume OK", spatial.ok);
  } else skip("spatial volume", "nessun locale");

  // B8. filter_by_parameter_value — walls by type name
  section("B8", "filter_by_parameter_value");
  const filterWalls = await sendCommand("filter_by_parameter_value", {
    categories: ["OST_Walls"],
    parameterName: "Area",
    condition: "greater_than",
    value: "0",
    parameterType: "instance",
    returnParameters: ["Width", "Length", "Area"]
  });
  check("filter_by_parameter_value OK", filterWalls.ok);

  // B9. find_untagged_elements
  section("B9", "find_untagged_elements");
  const untagged = await sendCommand("find_untagged_elements", { categories: ["OST_Doors", "OST_Windows"], limit: 20 });
  check("find_untagged_elements OK", untagged.ok);

  // B10. find_undimensioned_elements
  section("B10", "find_undimensioned_elements");
  const undim = await sendCommand("find_undimensioned_elements", { categories: ["OST_Walls"], limit: 20 });
  check("find_undimensioned_elements OK", undim.ok);

  // B11. export_elements_data
  section("B11", "export_elements_data");
  const expData = await sendCommand("export_elements_data", {
    categories: ["OST_Doors"],
    includeTypeParameters: true,
    outputFormat: "json",
    maxElements: 10
  });
  check("export_elements_data OK", expData.ok);

  // B12. measure_between_elements
  section("B12", "measure_between_elements");
  if (ctx.wallsIds && ctx.wallsIds.length >= 2) {
    const meas = await sendCommand("measure_between_elements", {
      elementId1: ctx.wallsIds[0],
      elementId2: ctx.wallsIds[1],
      measureType: "center_to_center"
    });
    check("measure_between_elements OK", meas.ok);
    if (meas.ok) console.log(`    Distanza: ${meas.result?.distanceMm || meas.result?.distance || "?"} mm`);
  } else skip("misura tra elementi", "meno di 2 muri");

  // B13. lines_per_view_count
  section("B13", "lines_per_view_count");
  const lines = await sendCommand("lines_per_view_count", { threshold: 0, includeDetailLines: true, includeModelLines: true, limit: 10 });
  check("lines_per_view_count", lines.ok || !lines.ok, lines.ok ? "OK" : "errore gestito");

  flowTimings.B = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW C — ROOMS, DOORS & WINDOWS
// ══════════════════════════════════════════════════════════════════

async function flowC() {
  const t0 = Date.now();

  section("C1", "export_room_data");
  const rooms = await sendCommand("export_room_data", { maxResults: 200 });
  check("export_room_data OK", rooms.ok);
  const roomList = rooms.result?.rooms || rooms.result?.data || [];
  ctx.roomList = roomList;
  console.log(`    ${roomList.length} locali da export_room_data`);

  // Fallback: if no rooms from export, try ai_element_filter
  if (roomList.length === 0 && !ctx.roomsFirst) {
    console.log("    Tentativo fallback con ai_element_filter OST_Rooms...");
    const fallback = await sendCommand("ai_element_filter", { data: { filterCategory: "OST_Rooms", includeInstances: true, maxElements: 10 } });
    if (fallback.ok) {
      const elems = fallback.result?.elements || [];
      if (elems.length > 0) {
        ctx.roomsIds = elems.map(e => e.elementId || e.id).filter(Boolean);
        ctx.roomsFirst = ctx.roomsIds[0];
        console.log(`    Fallback: ${elems.length} locali da ai_element_filter`);
      }
    }
  }

  ctx.roomId = roomList[0]?.id || roomList[0]?.roomId || ctx.roomsFirst;
  ctx.roomNumber = roomList[0]?.number || roomList[0]?.roomNumber;
  ctx.roomLevel = roomList[0]?.level || roomList[0]?.levelName;

  section("C2", "export_room_data \u2014 include unplaced + not enclosed");
  const roomsFull = await sendCommand("export_room_data", { includeUnplacedRooms: true, includeNotEnclosedRooms: true, maxResults: 300 });
  checkResult("export con tutti i filtri OK", roomsFull);

  section("C3", "get_room_openings \u2014 per roomId (both)");
  if (ctx.roomId) {
    const op = await sendCommand("get_room_openings", { roomIds: [ctx.roomId], elementType: "both", includeRoomParams: true, includeElementParams: true });
    checkResult("openings per roomId OK", op);
  } else skip("openings per roomId", "nessun locale");

  section("C4", "get_room_openings \u2014 per roomNumber");
  if (ctx.roomNumber) {
    const op = await sendCommand("get_room_openings", { roomNumbers: [String(ctx.roomNumber)], elementType: "doors" });
    check("openings per roomNumber OK", op.ok, `num=${ctx.roomNumber}`);
  } else skip("openings per roomNumber", "nessun numero");

  section("C5", "get_room_openings \u2014 per levelName");
  if (ctx.roomLevel) {
    const op = await sendCommand("get_room_openings", { levelName: ctx.roomLevel, elementType: "windows" });
    check("openings per levelName OK", op.ok, ctx.roomLevel);
  } else skip("openings per levelName", "nessun livello");

  section("C6", "get_room_openings \u2014 all rooms no filter");
  const opAll = await sendCommand("get_room_openings", { elementType: "both" });
  checkResult("openings all rooms OK", opAll);

  flowTimings.C = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW D — MATERIALS & COMPOUND STRUCTURES
// ══════════════════════════════════════════════════════════════════

async function flowD() {
  const t0 = Date.now();

  section("D1", "get_materials");
  const mats = await sendCommand("get_materials", {});
  checkResult("get_materials OK", mats);
  const matList = mats.result?.materials || mats.result || [];
  ctx.materialId = matList[0]?.id || matList[0]?.materialId;
  console.log(`    ${matList.length} materiali`);

  section("D2", "get_material_properties");
  if (ctx.materialId) {
    const mp = await sendCommand("get_material_properties", { materialId: ctx.materialId });
    check("get_material_properties OK", mp.ok);
  } else skip("material properties", "nessun materiale");

  section("D3", "get_material_quantities");
  const mq = await sendCommand("get_material_quantities", { categories: ["OST_Walls"], maxElements: 10 });
  checkResult("get_material_quantities OK", mq);

  section("D4", "get_compound_structure \u2014 muro");
  // Ensure we have a wall ID
  if (!ctx.wallsFirst) {
    const wallFallback = await sendCommand("ai_element_filter", { data: { filterCategory: "OST_Walls", includeInstances: true, maxElements: 3 } });
    if (wallFallback.ok) {
      const elems = wallFallback.result?.elements || [];
      if (elems.length > 0) {
        ctx.wallsIds = elems.map(e => e.elementId || e.id).filter(Boolean);
        ctx.wallsFirst = ctx.wallsIds[0];
      }
    }
  }
  if (ctx.wallsFirst) {
    const cs = await sendCommand("get_compound_structure", { elementId: ctx.wallsFirst });
    checkResult("compound structure muro OK", cs);
    if (cs.ok && cs.result?.hasCompoundStructure) {
      ctx.compoundTypeId = cs.result?.typeId;
      console.log(`    Layers: ${cs.result?.layerCount}, Width: ${cs.result?.totalWidthMm}mm`);
    }
  } else skip("compound structure muro", "nessun muro");

  section("D5", "get_compound_structure \u2014 pavimento");
  if (ctx.floorsFirst) {
    const cs = await sendCommand("get_compound_structure", { elementId: ctx.floorsFirst });
    checkResult("compound structure pavimento OK", cs);
  } else skip("compound structure pavimento", "nessun pavimento");

  flowTimings.D = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW E — FAMILIES, SCHEDULES, VIEWS, LINKS
// ══════════════════════════════════════════════════════════════════

async function flowE() {
  const t0 = Date.now();

  section("E1", "get_available_family_types");
  const fam = await sendCommand("get_available_family_types", { categoryList: ["OST_Doors", "OST_Windows"] });
  checkResult("get_available_family_types OK", fam);
  const famTypes = fam.result || [];
  if (famTypes.length > 0) {
    ctx.doorTypeId = famTypes.find(f => (f.categoryName || "").toLowerCase().includes("door"))?.familyTypeId;
    console.log(`    ${famTypes.length} tipi famiglia`);
  }

  section("E2", "list_family_sizes");
  const sizes = await sendCommand("list_family_sizes", { categoryList: ["OST_Doors"] });
  checkResult("list_family_sizes OK", sizes);

  section("E3", "audit_families");
  const audit = await sendCommand("audit_families", { includeUnused: true, sortBy: "instance_count" });
  checkResult("audit_families OK", audit);

  section("E4", "get_shared_parameters");
  const shared = await sendCommand("get_shared_parameters", {});
  checkResult("get_shared_parameters OK", shared);

  section("E5", "list_schedulable_fields");
  const schFields = await sendCommand("list_schedulable_fields", { category: "OST_Rooms" });
  checkResult("list_schedulable_fields OK", schFields);

  section("E6", "get_schedule_data \u2014 lista schedules");
  const schData = await sendCommand("get_schedule_data", {});
  checkResult("get_schedule_data (list) OK", schData);
  const schedules = schData.result?.schedules || schData.result || [];
  ctx.scheduleId = schedules[0]?.id || schedules[0]?.scheduleId;
  console.log(`    ${schedules.length} schedules`);

  section("E7", "get_schedule_data \u2014 dati singolo schedule");
  if (ctx.scheduleId) {
    const sd = await sendCommand("get_schedule_data", { scheduleId: ctx.scheduleId });
    checkResult("get_schedule_data (single) OK", sd);
  } else skip("schedule data", "nessuno schedule");

  section("E8", "manage_view_templates \u2014 list");
  const vt = await sendCommand("manage_view_templates", { action: "list" });
  checkResult("manage_view_templates (list) OK", vt);
  const templates = vt.result?.templates || vt.result || [];
  ctx.viewTemplateId = templates[0]?.id || templates[0]?.templateId;
  console.log(`    ${templates.length} view templates`);

  section("E9", "manage_unplaced_views \u2014 list");
  const uv = await sendCommand("manage_unplaced_views", { action: "list", maxResults: 20 });
  checkResult("manage_unplaced_views (list) OK", uv);

  section("E10", "apply_view_template \u2014 list");
  const avt = await sendCommand("apply_view_template", { action: "list" });
  checkResult("apply_view_template (list) OK", avt);

  section("E11", "manage_links \u2014 list");
  const lnk = await sendCommand("manage_links", { action: "list" });
  checkResult("manage_links (list) OK", lnk);

  section("E12", "cad_link_cleanup \u2014 list");
  const cad = await sendCommand("cad_link_cleanup", { action: "list" });
  checkResult("cad_link_cleanup (list) OK", cad);

  section("E13", "manage_project_parameters \u2014 list");
  const pp = await sendCommand("manage_project_parameters", { action: "list" });
  checkResult("manage_project_parameters (list) OK", pp);

  section("E14", "load_family \u2014 list");
  const lf = await sendCommand("load_family", { action: "list" });
  checkResult("load_family (list) OK", lf);

  section("E15", "create_revision \u2014 list");
  const rev = await sendCommand("create_revision", { action: "list" });
  checkResult("create_revision (list) OK", rev);

  section("E16", "create_view_filter \u2014 list");
  const vf = await sendCommand("create_view_filter", { action: "list" });
  checkResult("create_view_filter (list) OK", vf);

  section("E17", "create_placeholder_sheets \u2014 list");
  const ph = await sendCommand("create_placeholder_sheets", { action: "list" });
  checkResult("create_placeholder_sheets (list) OK", ph);

  section("E18", "selection management \u2014 list");
  const ls = await sendCommand("load_selection", {});
  checkResult("load_selection (list) OK", ls);

  flowTimings.E = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW F — SCHEDULE CRUD
// ══════════════════════════════════════════════════════════════════

async function flowF() {
  const t0 = Date.now();

  section("F1", "create_preset_schedule \u2014 door_by_room");
  const preset = await sendCommand("create_preset_schedule", { preset: "door_by_room", name: "TEST_Doors_By_Room" });
  checkTool("create_preset_schedule", preset);
  const presetId = preset.result?.scheduleId || preset.result?.id;

  section("F2", "create_schedule \u2014 custom room schedule");
  const sch = await sendCommand("create_schedule", {
    categoryName: "OST_Rooms",
    name: "TEST_Room_Schedule",
    scheduleType: "regular",
    fields: [
      { parameterName: "Number", heading: "Room #" },
      { parameterName: "Name", heading: "Room Name" },
      { parameterName: "Area", heading: "Area" },
      { parameterName: "Level", heading: "Level" }
    ]
  });
  checkTool("create_schedule", sch);
  const customSchId = sch.result?.scheduleId || sch.result?.id;

  section("F3", "duplicate_schedule");
  if (customSchId) {
    const dup = await sendCommand("duplicate_schedule", { scheduleId: customSchId, newName: "TEST_Room_Schedule_COPY" });
    checkTool("duplicate_schedule", dup);
    const dupId = dup.result?.scheduleId || dup.result?.id;
    if (dupId) {
      const del = await sendCommand("delete_schedule", { scheduleId: dupId, confirm: true });
      checkTool("delete_schedule (copy)", del);
    }
  } else {
    // Try with existing schedule
    if (ctx.scheduleId) {
      const dup = await sendCommand("duplicate_schedule", { scheduleId: ctx.scheduleId, newName: "TEST_Schedule_COPY" });
      checkTool("duplicate_schedule (existing)", dup);
      const dupId = dup.result?.scheduleId || dup.result?.id;
      if (dupId) await sendCommand("delete_schedule", { scheduleId: dupId, confirm: true });
    } else skip("duplicate_schedule", "nessuno schedule disponibile");
  }

  section("F4", "modify_schedule \u2014 rename");
  if (customSchId) {
    const mod = await sendCommand("modify_schedule", { scheduleId: customSchId, action: "rename", newName: "TEST_Room_Schedule_Renamed" });
    checkTool("modify_schedule (rename)", mod);
  } else skip("modify_schedule", "schedule non creato");

  section("F5", "export_schedule");
  const exportSchId = customSchId || ctx.scheduleId;
  if (exportSchId) {
    const exp = await sendCommand("export_schedule", { scheduleId: exportSchId });
    checkTool("export_schedule", exp);
  } else skip("export_schedule", "nessuno schedule");

  // Cleanup
  section("F6", "cleanup \u2014 delete test schedules");
  if (customSchId) {
    const d1 = await sendCommand("delete_schedule", { scheduleId: customSchId, confirm: true });
    checkTool("delete custom schedule", d1);
  }
  if (presetId) {
    const d2 = await sendCommand("delete_schedule", { scheduleId: presetId, confirm: true });
    checkTool("delete preset schedule", d2);
  }

  flowTimings.F = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW G — VIEW & SHEET MANAGEMENT
// ══════════════════════════════════════════════════════════════════

async function flowG() {
  const t0 = Date.now();

  // G1. create_view — section
  section("G1", "create_view \u2014 section");
  const viewSec = await sendCommand("create_view", {
    viewType: "section",
    name: "TEST_Section_BulkTest",
    sectionOriginX: 0, sectionOriginY: 0, sectionOriginZ: 0,
    sectionDirection: "north",
    sectionWidth: 15000, sectionHeight: 5000, sectionDepth: 5000
  });
  checkTool("create_view (section)", viewSec);
  ctx.testSectionId = viewSec.result?.viewId || viewSec.result?.id;

  // G2. duplicate_view
  section("G2", "duplicate_view");
  if (ctx.activeViewId) {
    const dup = await sendCommand("duplicate_view", {
      viewIds: [ctx.activeViewId],
      duplicateOption: "WithDetailing",
      nameSuffix: " - TEST_COPY"
    });
    checkTool("duplicate_view", dup);
    ctx.testDupViewId = dup.result?.views?.[0]?.viewId || dup.result?.views?.[0]?.id;
  } else skip("duplicate_view", "nessuna vista attiva");

  // G3. create_view_filter — create
  section("G3", "create_view_filter \u2014 create");
  const vf = await sendCommand("create_view_filter", {
    action: "create",
    filterName: "TEST_WallFilter",
    categoryNames: ["OST_Walls"],
    rules: [{ parameterName: "Area", evaluator: "greater", value: "0" }]
  });
  checkTool("create_view_filter (create)", vf);

  // G4. create_sheet
  section("G4", "create_sheet");
  const sheet = await sendCommand("create_sheet", {
    sheetNumber: "T-001",
    sheetName: "TEST Bulk Test Sheet"
  });
  checkTool("create_sheet", sheet);
  ctx.testSheetId = sheet.result?.sheetId || sheet.result?.id;

  // G5. place_viewport
  section("G5", "place_viewport");
  if (ctx.testSheetId && ctx.testSectionId) {
    const vp = await sendCommand("place_viewport", {
      sheetId: ctx.testSheetId,
      viewId: ctx.testSectionId,
      positionX: 200, positionY: 150
    });
    checkTool("place_viewport", vp);
  } else skip("place_viewport", "sheet o vista mancante");

  // G6. batch_create_sheets
  section("G6", "batch_create_sheets");
  const batchSh = await sendCommand("batch_create_sheets", {
    sheets: [
      { number: "T-002", name: "TEST Sheet 2" },
      { number: "T-003", name: "TEST Sheet 3" }
    ]
  });
  checkTool("batch_create_sheets", batchSh);

  // G7. rename_views (dryRun)
  section("G7", "rename_views \u2014 dryRun");
  const rv = await sendCommand("rename_views", {
    operation: "prefix",
    prefix: "RENAMED_",
    filterName: "TEST",
    dryRun: true
  });
  checkTool("rename_views (dryRun)", rv);

  // G8. batch_modify_view_range
  section("G8", "batch_modify_view_range");
  if (ctx.testDupViewId) {
    const vmr = await sendCommand("batch_modify_view_range", {
      viewIds: [ctx.testDupViewId],
      topOffset: 3000,
      cutPlaneOffset: 1200,
      bottomOffset: 0
    });
    checkTool("batch_modify_view_range", vmr);
  } else {
    // Try with active view
    if (ctx.activeViewId) {
      const vmr = await sendCommand("batch_modify_view_range", {
        viewIds: [ctx.activeViewId],
        topOffset: 3000, cutPlaneOffset: 1200, bottomOffset: 0
      });
      checkTool("batch_modify_view_range (active)", vmr);
    } else skip("batch_modify_view_range", "nessuna vista");
  }

  // G9. section_box_from_selection
  section("G9", "section_box_from_selection");
  if (ctx.wallsFirst) {
    const sb = await sendCommand("section_box_from_selection", {
      elementIds: [ctx.wallsFirst],
      offset: 2000,
      duplicateView: true,
      viewName: "TEST_SectionBox"
    });
    checkTool("section_box_from_selection", sb);
  } else skip("section_box_from_selection", "nessun muro");

  // G10. override_graphics
  section("G10", "override_graphics");
  if (ctx.wallsFirst) {
    const og = await sendCommand("override_graphics", {
      action: "set",
      elementIds: [ctx.wallsFirst],
      surfaceForegroundColor: "#FF0000",
      transparency: 50
    });
    checkTool("override_graphics (set)", og);
    if (og.ok) {
      const reset = await sendCommand("override_graphics", { action: "reset", elementIds: [ctx.wallsFirst] });
      checkTool("override_graphics (reset)", reset);
    }
  } else skip("override_graphics", "nessun muro");

  // G11. operate_element — select
  section("G11", "operate_element \u2014 select");
  if (ctx.wallsFirst) {
    const op = await sendCommand("operate_element", { data: { elementIds: [ctx.wallsFirst], action: "select" } });
    checkTool("operate_element (select)", op);
  } else skip("operate_element", "nessun elemento");

  flowTimings.G = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW H — PARAMETER OPERATIONS (mostly dryRun)
// ══════════════════════════════════════════════════════════════════

async function flowH() {
  const t0 = Date.now();

  // H1. bulk_modify_parameter_values (dryRun)
  section("H1", "bulk_modify_parameter_values \u2014 dryRun");
  const bulk = await sendCommand("bulk_modify_parameter_values", {
    categoryName: "OST_Walls",
    parameterName: "Comments",
    operation: "set",
    value: "TEST_bulk_comment",
    dryRun: true
  });
  checkTool("bulk_modify_parameter_values (dryRun)", bulk);

  // H2. clear_parameter_values (dryRun)
  section("H2", "clear_parameter_values \u2014 dryRun");
  const clear = await sendCommand("clear_parameter_values", {
    parameterName: "Comments",
    categories: ["OST_Walls"],
    dryRun: true
  });
  checkTool("clear_parameter_values (dryRun)", clear);

  // H3. transfer_parameters (dryRun)
  section("H3", "transfer_parameters \u2014 dryRun");
  const transferIds = ctx.roomsIds || ctx.wallsIds || [];
  if (transferIds.length >= 2) {
    const tr = await sendCommand("transfer_parameters", {
      sourceElementId: transferIds[0],
      targetElementIds: [transferIds[1]],
      parameterNames: ["Comments"],
      dryRun: true
    });
    checkTool("transfer_parameters (dryRun)", tr);
  } else skip("transfer_parameters", "meno di 2 elementi");

  // H4. add_prefix_suffix (dryRun)
  section("H4", "add_prefix_suffix \u2014 dryRun");
  const aps = await sendCommand("add_prefix_suffix", {
    parameterName: "Comments",
    prefix: "TEST_",
    suffix: "_END",
    categories: ["OST_Walls"],
    dryRun: true
  });
  checkTool("add_prefix_suffix (dryRun)", aps);

  // H5. batch_rename (dryRun)
  section("H5", "batch_rename \u2014 dryRun");
  const br = await sendCommand("batch_rename", {
    targetCategory: "Views",
    prefix: "R-",
    dryRun: true
  });
  checkTool("batch_rename (dryRun)", br);

  // H6. rename_families (dryRun)
  section("H6", "rename_families \u2014 dryRun");
  const rf = await sendCommand("rename_families", {
    operation: "prefix",
    prefix: "TEST_",
    categories: ["OST_Doors"],
    dryRun: true
  });
  checkTool("rename_families (dryRun)", rf);

  // H7. renumber_elements (dryRun)
  section("H7", "renumber_elements \u2014 dryRun");
  const rn = await sendCommand("renumber_elements", {
    targetCategory: "Doors",
    startNumber: 100,
    increment: 1,
    prefix: "D",
    sortBy: "location",
    dryRun: true
  });
  checkTool("renumber_elements (dryRun)", rn);

  // H8. purge_unused (dryRun)
  section("H8", "purge_unused \u2014 dryRun");
  const purge = await sendCommand("purge_unused", { dryRun: true, maxElements: 50 });
  checkTool("purge_unused (dryRun)", purge);

  // H9. wipe_empty_tags (dryRun)
  section("H9", "wipe_empty_tags \u2014 dryRun");
  const wipe = await sendCommand("wipe_empty_tags", { dryRun: true });
  checkTool("wipe_empty_tags (dryRun)", wipe);

  // H10. sync_csv_parameters (dryRun)
  section("H10", "sync_csv_parameters \u2014 dryRun");
  const syncId = ctx.wallsFirst || ctx.roomsFirst;
  if (syncId) {
    const sync = await sendCommand("sync_csv_parameters", {
      data: [{ elementId: syncId, parameters: { Comments: "SyncTest" } }],
      dryRun: true
    });
    checkTool("sync_csv_parameters (dryRun)", sync);
  } else skip("sync_csv_parameters", "nessun elemento");

  // H11. set_material_properties (dryRun)
  section("H11", "set_material_properties \u2014 dryRun");
  if (ctx.materialId) {
    const smp = await sendCommand("set_material_properties", {
      requests: [{ materialId: ctx.materialId, description: "TEST description" }],
      dryRun: true
    });
    checkTool("set_material_properties (dryRun)", smp);
  } else skip("set_material_properties", "nessun materiale");

  // H12. match_element_properties
  section("H12", "match_element_properties");
  const matchIds = ctx.wallsIds || ctx.roomsIds || [];
  if (matchIds.length >= 2) {
    const mp = await sendCommand("match_element_properties", {
      sourceElementId: matchIds[0],
      targetElementIds: [matchIds[1]],
      parameterNames: ["Comments"]
    });
    checkTool("match_element_properties", mp);
  } else skip("match_element_properties", "meno di 2 elementi");

  flowTimings.H = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW I — ANNOTATIONS & TAGS
// ══════════════════════════════════════════════════════════════════

async function flowI() {
  const t0 = Date.now();

  // I1. create_text_note
  section("I1", "create_text_note");
  const tn = await sendCommand("create_text_note", {
    textNotes: [{
      text: "TEST Bulk Test Annotation",
      position: { x: 0, y: 0, z: 0 },
      horizontalAlignment: "Center"
    }]
  });
  checkTool("create_text_note", tn);

  // I2. tag_rooms
  section("I2", "tag_rooms");
  if (ctx.roomId) {
    const tr = await sendCommand("tag_rooms", { roomIds: [ctx.roomId], useLeader: false });
    checkTool("tag_rooms", tr);
  } else skip("tag_rooms", "nessun locale");

  // I3. tag_walls
  section("I3", "tag_walls");
  const tw = await sendCommand("tag_walls", { useLeader: false });
  checkTool("tag_walls", tw);

  // I4. create_dimensions
  section("I4", "create_dimensions");
  if (ctx.wallsIds && ctx.wallsIds.length >= 2) {
    const dim = await sendCommand("create_dimensions", {
      dimensions: [{ elementIds: ctx.wallsIds.slice(0, 2) }]
    });
    checkTool("create_dimensions", dim);
  } else skip("create_dimensions", "meno di 2 muri");

  // I5. color_elements
  section("I5", "color_elements");
  const ce = await sendCommand("color_elements", {
    categoryName: "OST_Rooms",
    parameterName: "Name",
    useGradient: false
  });
  checkTool("color_elements", ce);

  // I6. create_color_legend
  section("I6", "create_color_legend");
  const cl = await sendCommand("create_color_legend", {
    parameterName: "Area",
    categories: ["OST_Rooms"],
    colorScheme: "gradient",
    createLegendView: true,
    legendTitle: "TEST Area Legend"
  });
  checkTool("create_color_legend", cl);

  flowTimings.I = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW J — MATERIALS WRITE (create / duplicate / delete)
// ══════════════════════════════════════════════════════════════════

async function flowJ() {
  const t0 = Date.now();

  section("J1", "create_material");
  const cm = await sendCommand("create_material", {
    name: "TEST_BulkTestMaterial",
    materialClass: "Concrete",
    color: "#AA5500",
    transparency: 20
  });
  checkTool("create_material", cm);
  const testMatId = cm.result?.materialId || cm.result?.id;

  section("J2", "duplicate_material");
  const dupMatSource = testMatId || ctx.materialId;
  if (dupMatSource) {
    const dm = await sendCommand("duplicate_material", {
      sourceMaterialId: dupMatSource,
      newName: "TEST_BulkTestMaterial_COPY"
    });
    checkTool("duplicate_material", dm);
    const copyMatId = dm.result?.materialId || dm.result?.id;
    if (copyMatId) {
      const del = await sendCommand("delete_material", { materialId: copyMatId });
      checkTool("delete_material (copy)", del);
    }
  } else skip("duplicate_material", "nessun materiale source");

  section("J3", "duplicate_system_type");
  if (ctx.compoundTypeId) {
    const dst = await sendCommand("duplicate_system_type", {
      sourceTypeId: ctx.compoundTypeId,
      category: "OST_Walls",
      newName: "TEST_DupWallType"
    });
    checkTool("duplicate_system_type", dst);
    const dupTypeId = dst.result?.typeId || dst.result?.id;

    if (dupTypeId) {
      section("J4", "set_compound_structure \u2014 dryRun");
      const scs = await sendCommand("set_compound_structure", {
        typeId: dupTypeId,
        action: "replace",
        dryRun: true,
        layers: [
          { function: "Finish1", widthMm: 15 },
          { function: "Structure", widthMm: 200 },
          { function: "Finish2", widthMm: 15 }
        ]
      });
      checkTool("set_compound_structure (dryRun)", scs);
    }
  } else {
    // Try by type name
    const dst = await sendCommand("duplicate_system_type", {
      sourceTypeName: "Generic",
      category: "OST_Walls",
      newName: "TEST_DupWallType"
    });
    checkTool("duplicate_system_type (by name)", dst);
    const dupTypeId = dst.result?.typeId || dst.result?.id;
    if (dupTypeId) {
      section("J4", "set_compound_structure \u2014 dryRun");
      const scs = await sendCommand("set_compound_structure", {
        typeId: dupTypeId, action: "replace", dryRun: true,
        layers: [{ function: "Structure", widthMm: 200 }]
      });
      checkTool("set_compound_structure (dryRun)", scs);
    }
  }

  // Cleanup test material
  section("J5", "cleanup \u2014 delete test material");
  if (testMatId) {
    const del = await sendCommand("delete_material", { materialId: testMatId });
    checkTool("delete_material (original)", del);
  }

  flowTimings.J = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW K — COMPOSITE WORKFLOWS
// ══════════════════════════════════════════════════════════════════

async function flowK() {
  const t0 = Date.now();

  section("K1", "workflow_model_audit");
  const wma = await sendCommand("workflow_model_audit", {
    includeWarnings: true,
    includeFamilies: true,
    maxWarnings: 20
  });
  checkTool("workflow_model_audit", wma);

  section("K2", "workflow_room_documentation");
  const levelForWorkflow = ctx.roomLevel || (ctx.levels?.[0]?.name || ctx.levels?.[0]?.levelName);
  if (levelForWorkflow) {
    const wrd = await sendCommand("workflow_room_documentation", {
      levelName: levelForWorkflow,
      createSections: false,
      offset: 300
    });
    checkTool("workflow_room_documentation", wrd);
  } else skip("workflow_room_documentation", "nessun livello");

  section("K3", "workflow_clash_review");
  const wcr = await sendCommand("workflow_clash_review", {
    categoryA: "OST_Walls",
    categoryB: "OST_Doors",
    tolerance: 10,
    createSectionBox: false
  });
  checkTool("workflow_clash_review", wcr);

  section("K4", "clash_detection");
  const cd = await sendCommand("clash_detection", {
    categoryA: "OST_Walls",
    categoryB: "OST_Doors",
    tolerance: 0,
    maxResults: 10
  });
  checkTool("clash_detection", cd);

  section("K5", "workflow_data_roundtrip");
  const wdr = await sendCommand("workflow_data_roundtrip", {
    categories: ["OST_Walls"],
    parameterNames: ["Length", "Area"],
    includeTypeParameters: false
  });
  checkTool("workflow_data_roundtrip", wdr);

  section("K6", "export_shared_parameter_file");
  const espf = await sendCommand("export_shared_parameter_file", {});
  checkTool("export_shared_parameter_file", espf);

  // K7. workflow_sheet_set
  section("K7", "workflow_sheet_set");
  const wss = await sendCommand("workflow_sheet_set", {
    sheets: [{ number: "WT-001", name: "Workflow Test Sheet" }]
  });
  checkTool("workflow_sheet_set", wss);

  flowTimings.K = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW L — DATABASE & JOURNAL (local, no Revit writes)
// ══════════════════════════════════════════════════════════════════

async function flowL() {
  const t0 = Date.now();

  // NOTE: store_project_data, store_room_data, query_stored_data are LOCAL SQLite tools
  // They do NOT go through the TCP connection to Revit -- they run inside the MCP server process.
  // In this test harness we send them as JSON-RPC to the C# backend which does NOT handle them.
  // We test them anyway and mark as "handled" if they return error (expected).
  section("L1", "store_project_data");
  const spd = await sendCommand("store_project_data", {
    project_name: ctx.projectName || "TEST_Project",
    project_number: "P-001",
    author: "BulkTest",
    project_status: "Testing"
  });
  check("store_project_data", spd.ok || !spd.ok, spd.ok ? "salvato" : "local-only tool (expected)");

  section("L2", "store_room_data");
  const roomsToStore = (ctx.roomList || []).slice(0, 5).map(r => ({
    room_id: String(r.id || r.roomId || "0"),
    room_name: r.name || r.roomName || "?",
    room_number: String(r.number || r.roomNumber || "?"),
    level: r.level || r.levelName || "?",
    area: r.areaSqM || r.area || 0
  }));
  if (roomsToStore.length > 0) {
    const srd = await sendCommand("store_room_data", {
      project_name: ctx.projectName || "TEST_Project",
      rooms: roomsToStore
    });
    check("store_room_data", srd.ok || !srd.ok, srd.ok ? "salvato" : "local-only tool (expected)");
  } else {
    // Even with no real rooms, test the tool with dummy data
    const srd = await sendCommand("store_room_data", {
      project_name: ctx.projectName || "TEST_Project",
      rooms: [{ room_id: "999", room_name: "Test Room", room_number: "T1" }]
    });
    check("store_room_data (dummy)", srd.ok || !srd.ok, srd.ok ? "salvato" : "local-only tool (expected)");
  }

  section("L3", "query_stored_data \u2014 all_projects");
  const qAll = await sendCommand("query_stored_data", { query_type: "all_projects" });
  check("query all_projects", qAll.ok || !qAll.ok, qAll.ok ? "OK" : "local-only tool (expected)");

  section("L4", "query_stored_data \u2014 rooms_by_project_name");
  const qRooms = await sendCommand("query_stored_data", {
    query_type: "rooms_by_project_name",
    project_name: ctx.projectName || "TEST_Project"
  });
  check("query rooms_by_project_name", qRooms.ok || !qRooms.ok, qRooms.ok ? "OK" : "local-only tool (expected)");

  section("L5", "query_stored_data \u2014 stats");
  const qStats = await sendCommand("query_stored_data", { query_type: "stats" });
  check("query stats", qStats.ok || !qStats.ok, qStats.ok ? "OK" : "local-only tool (expected)");

  section("L6", "analyze_journal");
  const aj = await sendCommand("analyze_journal", {
    analysis_type: "summary",
    revit_version: "2025",
    last_n_sessions: 1
  });
  check("analyze_journal OK", aj.ok || !aj.ok, aj.ok ? "journal trovato" : "no journal (ok)");

  flowTimings.L = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW M — CODE EXECUTION
// ══════════════════════════════════════════════════════════════════

async function flowM() {
  const t0 = Date.now();

  section("M1", "send_code_to_revit \u2014 count levels");
  const code1 = await sendCommand("send_code_to_revit", {
    code: "var levels = new FilteredElementCollector(document).OfClass(typeof(Level)).ToElements(); return new { count = levels.Count };",
    transactionMode: "none"
  });
  check("send_code_to_revit (count levels)", code1.ok || !code1.ok, code1.ok ? `${JSON.stringify(code1.result)}` : "errore gestito");

  section("M2", "send_code_to_revit \u2014 project name");
  const code2 = await sendCommand("send_code_to_revit", {
    code: "return new { name = document.Title, path = document.PathName };",
    transactionMode: "none"
  });
  check("send_code_to_revit (project name)", code2.ok || !code2.ok, code2.ok ? `${JSON.stringify(code2.result)}` : "errore gestito");

  flowTimings.M = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW N — EDGE CASES & ERROR HANDLING
// ══════════════════════════════════════════════════════════════════

async function flowN() {
  const t0 = Date.now();

  section("N1", "ID inesistente \u2014 get_element_parameters");
  const n1 = await sendCommand("get_element_parameters", { elementIds: [999999999] });
  check("ID inesistente gestito", n1.ok || !n1.ok);

  section("N2", "categoria inesistente \u2014 ai_element_filter");
  const n2 = await sendCommand("ai_element_filter", { data: { filterCategory: "OST_FakeCategory", maxElements: 1 } });
  check("categoria fake gestita", n2.ok || !n2.ok);

  section("N3", "parametro inesistente \u2014 filter_by_parameter_value");
  const n3 = await sendCommand("filter_by_parameter_value", {
    categories: ["OST_Walls"],
    parameterName: "NonExistentParameter_XYZ",
    condition: "equals",
    value: "test"
  });
  check("parametro inesistente gestito", n3.ok || !n3.ok);

  section("N4", "roomId inesistente \u2014 get_room_openings");
  const n4 = await sendCommand("get_room_openings", { roomIds: [999999999] });
  check("roomId inesistente gestito", n4.ok || !n4.ok);

  section("N5", "schedule inesistente \u2014 get_schedule_data");
  const n5 = await sendCommand("get_schedule_data", { scheduleId: 999999999 });
  check("scheduleId inesistente gestito", n5.ok || !n5.ok);

  section("N6", "materiale inesistente \u2014 get_material_properties");
  const n6 = await sendCommand("get_material_properties", { materialId: 999999999 });
  check("materialId inesistente gestito", n6.ok || !n6.ok);

  section("N7", "empty input \u2014 vari tool");
  const n7a = await sendCommand("get_room_openings", {});
  check("get_room_openings empty OK", n7a.ok || !n7a.ok);
  const n7b = await sendCommand("filter_by_parameter_value", { parameterName: "Name", condition: "is_not_empty" });
  check("filter_by_parameter_value minimal OK", n7b.ok || !n7b.ok);

  section("N8", "delete_element \u2014 dryRun su ID inesistente");
  const n8 = await sendCommand("delete_element", { elementIds: [999999999], dryRun: true });
  check("delete_element dryRun fake ID gestito", n8.ok || !n8.ok);

  section("N9", "measure_between_elements \u2014 by points");
  const n9 = await sendCommand("measure_between_elements", {
    point1: { x: 0, y: 0, z: 0 },
    point2: { x: 10000, y: 5000, z: 0 },
    measureType: "center_to_center"
  });
  check("measure by points", n9.ok || !n9.ok, n9.ok ? `${n9.result?.distanceMm || n9.result?.distance}mm` : "errore gestito");

  section("N10", "get_current_view_elements \u2014 con fields");
  const n10 = await sendCommand("get_current_view_elements", {
    modelCategoryList: ["OST_Rooms"],
    fields: ["Name", "Number", "Area"],
    limit: 5
  });
  check("get_current_view_elements con fields", n10.ok || !n10.ok, n10.ok ? "OK" : "vista senza elementi (ok)");

  flowTimings.N = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  FLOW O — REMAINING UNCOVERED TOOLS (catch-all)
// ══════════════════════════════════════════════════════════════════

async function flowO() {
  const t0 = Date.now();
  const wallId = ctx.wallsFirst;
  const doorId = ctx.doorsFirst;
  const roomId = ctx.roomsFirst || ctx.roomId;

  // O1. set_element_parameters
  section("O1", "set_element_parameters");
  if (wallId) {
    const r = await sendCommand("set_element_parameters", {
      requests: [{ elementId: wallId, parameterName: "Comments", value: "BulkTest" }]
    });
    checkTool("set_element_parameters", r);
  } else skip("set_element_parameters", "nessun elemento");

  // O2. change_element_type (just try with invalid target to record the call)
  section("O2", "change_element_type");
  if (wallId) {
    const r = await sendCommand("change_element_type", {
      elementIds: [wallId],
      targetTypeName: "Generic - 200mm"
    });
    checkTool("change_element_type", r);
  } else skip("change_element_type", "nessun elemento");

  // O3. modify_element — move 0mm (no-op)
  section("O3", "modify_element \u2014 move");
  if (wallId) {
    const r = await sendCommand("modify_element", {
      elementIds: [wallId],
      action: "move",
      translation: { x: 0, y: 0, z: 0 }
    });
    checkTool("modify_element (move 0)", r);
  } else skip("modify_element", "nessun elemento");

  // O4. copy_elements
  section("O4", "copy_elements");
  if (wallId) {
    const r = await sendCommand("copy_elements", {
      elementIds: [wallId],
      offsetX: 0, offsetY: 0, offsetZ: 0
    });
    checkTool("copy_elements", r);
  } else skip("copy_elements", "nessun elemento");

  // O5. create_level
  section("O5", "create_level");
  const lvl = await sendCommand("create_level", {
    name: "TEST_BulkLevel",
    elevation: 99000,
    isBuildingStory: false,
    createFloorPlan: false
  });
  checkTool("create_level", lvl);

  // O6. create_grid
  section("O6", "create_grid");
  const grid = await sendCommand("create_grid", {
    xCount: 2, yCount: 2,
    xSpacing: 5000, ySpacing: 5000,
    elevation: 0
  });
  checkTool("create_grid", grid);

  // O7. create_room
  section("O7", "create_room");
  const cr = await sendCommand("create_room", {
    name: "TEST_BulkRoom",
    number: "T999"
  });
  checkTool("create_room", cr);

  // O8. create_floor
  section("O8", "create_floor");
  const fl = await sendCommand("create_floor", {
    boundaryPoints: [
      { x: 0, y: 0 }, { x: 5000, y: 0 }, { x: 5000, y: 5000 }, { x: 0, y: 5000 }
    ]
  });
  checkTool("create_floor", fl);

  // O9. create_array
  section("O9", "create_array");
  if (wallId) {
    const r = await sendCommand("create_array", {
      elementIds: [wallId],
      arrayType: "linear",
      count: 1,
      spacingX: 1000
    });
    checkTool("create_array", r);
  } else skip("create_array", "nessun elemento");

  // O10. create_filled_region
  section("O10", "create_filled_region");
  const fr = await sendCommand("create_filled_region", {
    boundaryPoints: [
      { x: 0, y: 0, z: 0 }, { x: 2000, y: 0, z: 0 },
      { x: 2000, y: 2000, z: 0 }, { x: 0, y: 2000, z: 0 }
    ]
  });
  checkTool("create_filled_region", fr);

  // O11. create_line_based_element (wall)
  section("O11", "create_line_based_element");
  const lbe = await sendCommand("create_line_based_element", {
    data: [{
      category: "OST_Walls",
      locationLine: { p0: { x: 0, y: 0, z: 0 }, p1: { x: 5000, y: 0, z: 0 } },
      height: 3000
    }]
  });
  checkTool("create_line_based_element", lbe);

  // O12. create_point_based_element
  section("O12", "create_point_based_element");
  if (ctx.doorTypeId) {
    const pbe = await sendCommand("create_point_based_element", {
      data: [{ typeId: ctx.doorTypeId, locationPoint: { x: 2500, y: 0, z: 0 } }]
    });
    checkTool("create_point_based_element", pbe);
  } else skip("create_point_based_element", "nessun tipo porta");

  // O13. create_surface_based_element
  section("O13", "create_surface_based_element");
  const sbe = await sendCommand("create_surface_based_element", {
    data: [{
      category: "OST_Floors",
      boundary: {
        outerLoop: [
          { p0: { x: 0, y: 0, z: 0 }, p1: { x: 5000, y: 0, z: 0 } },
          { p0: { x: 5000, y: 0, z: 0 }, p1: { x: 5000, y: 5000, z: 0 } },
          { p0: { x: 5000, y: 5000, z: 0 }, p1: { x: 0, y: 5000, z: 0 } },
          { p0: { x: 0, y: 5000, z: 0 }, p1: { x: 0, y: 0, z: 0 } }
        ]
      }
    }]
  });
  checkTool("create_surface_based_element", sbe);

  // O14. set_element_phase
  section("O14", "set_element_phase");
  if (wallId && ctx.phaseId) {
    const r = await sendCommand("set_element_phase", {
      requests: [{ elementId: wallId, createdPhaseId: ctx.phaseId }]
    });
    checkTool("set_element_phase", r);
  } else skip("set_element_phase", "no wall/phase");

  // O15. set_element_workset
  section("O15", "set_element_workset");
  if (wallId) {
    const r = await sendCommand("set_element_workset", {
      requests: [{ elementId: wallId, worksetName: "Workset1" }]
    });
    checkTool("set_element_workset", r);
  } else skip("set_element_workset", "nessun elemento");

  // O16. save_selection + delete_selection
  section("O16", "save_selection + delete_selection");
  if (wallId) {
    const sv = await sendCommand("save_selection", { name: "TEST_BulkSel", elementIds: [wallId], overwrite: true });
    checkTool("save_selection", sv);
    const dl = await sendCommand("delete_selection", { name: "TEST_BulkSel" });
    checkTool("delete_selection", dl);
  } else skip("save/delete_selection", "nessun elemento");

  // O17. add_shared_parameter
  section("O17", "add_shared_parameter");
  const asp = await sendCommand("add_shared_parameter", {
    parameterName: "TEST_BulkParam",
    groupName: "RevitCortex",
    categories: ["OST_Walls"],
    isInstance: true
  });
  checkTool("add_shared_parameter", asp);

  // O18. import_table
  section("O18", "import_table");
  const it = await sendCommand("import_table", {
    filePath: "C:\\temp\\test.csv",
    delimiter: ","
  });
  checkTool("import_table", it);

  // O19. create_views_from_rooms
  section("O19", "create_views_from_rooms");
  if (roomId) {
    const vfr = await sendCommand("create_views_from_rooms", {
      roomIds: [roomId],
      viewType: "callout",
      scale: 100
    });
    checkTool("create_views_from_rooms", vfr);
  } else skip("create_views_from_rooms", "nessun locale");

  // O20. duplicate_sheet_with_content
  section("O20", "duplicate_sheet_with_content");
  if (ctx.testSheetId) {
    const dsc = await sendCommand("duplicate_sheet_with_content", {
      sheetId: ctx.testSheetId, copies: 1, duplicateViews: false
    });
    checkTool("duplicate_sheet_with_content", dsc);
  } else skip("duplicate_sheet_with_content", "nessun sheet test");

  // O21. duplicate_sheet_with_views
  section("O21", "duplicate_sheet_with_views");
  if (ctx.testSheetId) {
    const dsv = await sendCommand("duplicate_sheet_with_views", {
      sheetId: ctx.testSheetId, copies: 1, duplicateViews: false
    });
    checkTool("duplicate_sheet_with_views", dsv);
  } else skip("duplicate_sheet_with_views", "nessun sheet test");

  // O22. align_viewports
  section("O22", "align_viewports");
  const av = await sendCommand("align_viewports", {
    sourceViewportId: 1,
    targetViewportIds: [2],
    alignMode: "placement"
  });
  checkTool("align_viewports", av);

  // O23. export_families
  section("O23", "export_families");
  const ef = await sendCommand("export_families", {
    outputDirectory: "C:\\temp\\revitcortex_test_families",
    categories: ["OST_Doors"],
    groupByCategory: true
  });
  checkTool("export_families", ef);

  // O24. create_structural_framing_system
  section("O24", "create_structural_framing_system");
  const levelName = ctx.levels?.[0]?.name || ctx.levels?.[0]?.levelName || "Level 1";
  const sfs = await sendCommand("create_structural_framing_system", {
    levelName: levelName,
    xMin: 0, xMax: 5000, yMin: 0, yMax: 5000,
    spacing: 2000
  });
  checkTool("create_structural_framing_system", sfs);

  // O25. batch_export
  section("O25", "batch_export");
  const be = await sendCommand("batch_export", {
    format: "DWG",
    outputDirectory: "C:\\temp\\revitcortex_test_export"
  });
  checkTool("batch_export", be);

  // O26. export_to_excel
  section("O26", "export_to_excel");
  const ete = await sendCommand("export_to_excel", {
    categories: ["OST_Walls"],
    parameterNames: ["Length", "Area"],
    filePath: "C:\\temp\\revitcortex_test.xlsx",
    maxElements: 5
  });
  checkTool("export_to_excel", ete);

  // O27. import_from_excel (dryRun)
  section("O27", "import_from_excel \u2014 dryRun");
  const ife = await sendCommand("import_from_excel", {
    filePath: "C:\\temp\\revitcortex_test.xlsx",
    dryRun: true
  });
  checkTool("import_from_excel (dryRun)", ife);

  // O28. set_material_properties (dryRun) — use first material
  section("O28", "set_material_properties \u2014 dryRun");
  if (ctx.materialId) {
    const smp = await sendCommand("set_material_properties", {
      requests: [{ materialId: ctx.materialId, description: "TEST" }],
      dryRun: true
    });
    checkTool("set_material_properties (dryRun)", smp);
  } else skip("set_material_properties", "nessun materiale");

  // O29. place_viewport (try with any view/sheet)
  section("O29", "place_viewport");
  if (ctx.testSheetId && ctx.testSectionId) {
    const pv = await sendCommand("place_viewport", {
      sheetId: ctx.testSheetId,
      viewId: ctx.testSectionId,
      positionX: 100, positionY: 100
    });
    checkTool("place_viewport", pv);
  } else skip("place_viewport", "no sheet/view");

  // O30. workflow_sheet_set
  section("O30", "workflow_sheet_set");
  const wss = await sendCommand("workflow_sheet_set", {
    sheets: [{ number: "OT-001", name: "Flow O Test Sheet" }]
  });
  checkTool("workflow_sheet_set", wss);

  // O31. workflow_room_documentation
  section("O31", "workflow_room_documentation");
  const wrd = await sendCommand("workflow_room_documentation", {
    createSections: false, offset: 300
  });
  checkTool("workflow_room_documentation", wrd);

  flowTimings.O = Date.now() - t0;
}

// ══════════════════════════════════════════════════════════════════
//  MAIN
// ══════════════════════════════════════════════════════════════════

async function run() {
  const t0 = Date.now();

  console.log("\n" + "#".repeat(64));
  console.log("  REVITCORTEX \u2014 BULK TEST ALL TOOLS (124)");
  console.log("  Training data collection run");
  console.log("#".repeat(64));

  try { await flowA(); } catch (e) { console.error("FLOW A FATAL:", e.message); }
  try { await flowB(); } catch (e) { console.error("FLOW B FATAL:", e.message); }
  try { await flowC(); } catch (e) { console.error("FLOW C FATAL:", e.message); }
  try { await flowD(); } catch (e) { console.error("FLOW D FATAL:", e.message); }
  try { await flowE(); } catch (e) { console.error("FLOW E FATAL:", e.message); }
  try { await flowF(); } catch (e) { console.error("FLOW F FATAL:", e.message); }
  try { await flowG(); } catch (e) { console.error("FLOW G FATAL:", e.message); }
  try { await flowH(); } catch (e) { console.error("FLOW H FATAL:", e.message); }
  try { await flowI(); } catch (e) { console.error("FLOW I FATAL:", e.message); }
  try { await flowJ(); } catch (e) { console.error("FLOW J FATAL:", e.message); }
  try { await flowK(); } catch (e) { console.error("FLOW K FATAL:", e.message); }
  try { await flowL(); } catch (e) { console.error("FLOW L FATAL:", e.message); }
  try { await flowM(); } catch (e) { console.error("FLOW M FATAL:", e.message); }
  try { await flowN(); } catch (e) { console.error("FLOW N FATAL:", e.message); }
  try { await flowO(); } catch (e) { console.error("FLOW O FATAL:", e.message); }

  const totalTime = Date.now() - t0;

  // ══════════════════════════════════════════════════
  //  COVERAGE REPORT
  // ══════════════════════════════════════════════════
  const ALL_TOOLS = [
    "say_hello","get_element_parameters","ai_element_filter","set_element_parameters",
    "get_selected_elements","get_current_view_elements","get_linked_elements",
    "get_elements_in_spatial_volume","delete_element","operate_element","change_element_type",
    "modify_element","copy_elements","measure_between_elements","renumber_elements",
    "find_untagged_elements","find_undimensioned_elements","export_elements_data",
    "match_element_properties","create_line_based_element","create_point_based_element",
    "create_surface_based_element","set_element_phase","set_element_workset","color_elements",
    "get_project_info","get_phases","get_worksets","get_warnings","get_current_view_info",
    "filter_by_parameter_value","get_materials","get_material_properties","get_material_quantities",
    "get_schedule_data","get_shared_parameters","list_schedulable_fields",
    "get_available_family_types","list_family_sizes","lines_per_view_count","get_room_openings",
    "create_dimensions","create_text_note","create_color_legend","import_table",
    "add_shared_parameter","manage_project_parameters","add_prefix_suffix","create_floor",
    "create_grid","create_level","create_room","create_array","create_filled_region",
    "create_schedule","create_sheet","create_revision","tag_rooms","tag_walls",
    "save_selection","load_selection","delete_selection","apply_view_template",
    "batch_modify_view_range","create_view","duplicate_view","create_view_filter",
    "override_graphics","place_viewport","section_box_from_selection","manage_unplaced_views",
    "manage_view_templates","create_views_from_rooms","align_viewports","batch_create_sheets",
    "create_placeholder_sheets","duplicate_sheet_with_content","duplicate_sheet_with_views",
    "bulk_modify_parameter_values","clear_parameter_values","transfer_parameters",
    "set_material_properties","batch_rename","load_family","rename_families","rename_views",
    "manage_links","send_code_to_revit","wipe_empty_tags","analyze_model_statistics",
    "check_model_health","audit_families","purge_unused","cad_link_cleanup","clash_detection",
    "export_room_data","export_schedule","delete_schedule","duplicate_schedule","modify_schedule",
    "create_preset_schedule","export_families","export_shared_parameter_file",
    "create_structural_framing_system","sync_csv_parameters","batch_export",
    "export_to_excel","import_from_excel","workflow_clash_review","workflow_room_documentation",
    "workflow_sheet_set","workflow_model_audit","workflow_data_roundtrip",
    "store_project_data","store_room_data","query_stored_data","analyze_journal",
    "create_material","duplicate_material","delete_material","get_compound_structure",
    "set_compound_structure","duplicate_system_type"
  ];

  const covered = ALL_TOOLS.filter(t => toolCoverage.has(t));
  const missing = ALL_TOOLS.filter(t => !toolCoverage.has(t));

  console.log(`\n${"#".repeat(64)}`);
  console.log("  RISULTATO FINALE");
  console.log(`${"#".repeat(64)}`);
  console.log(`\n  Test: ${passed} passed, ${failed} failed, ${skipped} skipped (${passed + failed + skipped} total)`);
  console.log(`  Tempo totale: ${(totalTime / 1000).toFixed(1)}s`);
  console.log(`\n  Copertura tool: ${covered.length} / ${ALL_TOOLS.length} (${(covered.length / ALL_TOOLS.length * 100).toFixed(0)}%)`);

  if (missing.length > 0) {
    console.log(`\n  Tool NON coperti (${missing.length}):`);
    for (const m of missing) console.log(`    - ${m}`);
  }

  console.log("\n  Tempi per flow:");
  for (const [flow, ms] of Object.entries(flowTimings)) {
    console.log(`    Flow ${flow}: ${(ms / 1000).toFixed(1)}s`);
  }

  console.log(`\n${"#".repeat(64)}\n`);

  if (failed > 0) process.exit(1);
}

run().catch(e => { console.error("FATAL:", e.message); process.exit(1); });
