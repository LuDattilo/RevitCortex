import { Socket } from "net";

const HOST = "localhost";
const PORT = 8080;
let requestId = 0;

function sendCommand(method, params = {}) {
  return new Promise((resolve, reject) => {
    const client = new Socket();
    const id = String(++requestId);
    let buffer = "";
    const timeout = setTimeout(() => { client.destroy(); reject(new Error(`TIMEOUT: ${method}`)); }, 60_000);
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

function pp(obj) { return JSON.stringify(obj, null, 2); }

async function run() {
  console.log("=== TEST MATERIALI E STRATIGRAFIE ===\n");

  // 1. Lista materiali (primi 10)
  console.log("── 1. get_materials (all) ──");
  const mats = await sendCommand("get_materials", {});
  if (mats.ok) {
    console.log(`  Totale materiali: ${mats.result.materialCount}`);
    console.log(`  Primi 5:`);
    for (const m of mats.result.materials.slice(0, 5)) {
      console.log(`    [${m.id}] ${m.name} (class: ${m.materialClass})`);
    }
  } else { console.log("  ERRORE:", pp(mats.error)); }

  // 2. Filtra materiali per classe "Concrete"
  console.log("\n── 2. get_materials (class: Concrete) ──");
  const concrete = await sendCommand("get_materials", { materialClass: "Concrete" });
  if (concrete.ok) {
    console.log(`  Trovati: ${concrete.result.materialCount}`);
    for (const m of concrete.result.materials.slice(0, 5)) {
      console.log(`    [${m.id}] ${m.name}`);
    }
  } else { console.log("  ERRORE:", pp(concrete.error)); }

  // 3. Filtra materiali per nome "brick"
  console.log("\n── 3. get_materials (name: brick) ──");
  const brick = await sendCommand("get_materials", { nameFilter: "brick" });
  if (brick.ok) {
    console.log(`  Trovati: ${brick.result.materialCount}`);
    for (const m of brick.result.materials) {
      console.log(`    [${m.id}] ${m.name} (class: ${m.materialClass})`);
    }
  } else { console.log("  ERRORE:", pp(brick.error)); }

  // 4. Proprietà dettagliate di un materiale
  console.log("\n── 4. get_material_properties (by name: Default) ──");
  const defMat = await sendCommand("get_material_properties", { materialName: "Default" });
  if (defMat.ok) { console.log(`  ${pp(defMat.result)}`); }
  else { console.log("  ERRORE:", pp(defMat.error)); }

  // 5. Proprietà di un materiale specifico (concrete se esiste)
  if (concrete.ok && concrete.result.materials.length > 0) {
    const cId = concrete.result.materials[0].id;
    console.log(`\n── 5. get_material_properties (id: ${cId} - ${concrete.result.materials[0].name}) ──`);
    const cProps = await sendCommand("get_material_properties", { materialId: cId });
    if (cProps.ok) { console.log(`  ${pp(cProps.result)}`); }
    else { console.log("  ERRORE:", pp(cProps.error)); }
  }

  // 6. Quantità materiali per muri
  console.log("\n── 6. get_material_quantities (OST_Walls) ──");
  const wallQuant = await sendCommand("get_material_quantities", { categoryFilters: ["OST_Walls"], maxResults: 10 });
  if (wallQuant.ok) {
    console.log(`  Totale materiali: ${wallQuant.result.totalMaterials}, Totale count: ${wallQuant.result.totalCount}`);
    console.log(`  Area totale: ${wallQuant.result.totalArea?.toFixed(2)}, Volume totale: ${wallQuant.result.totalVolume?.toFixed(2)}`);
    for (const m of (wallQuant.result.materials || wallQuant.result.items || []).slice(0, 5)) {
      console.log(`    ${m.name}: area=${m.area?.toFixed(2)}, vol=${m.volume?.toFixed(2)}, count=${m.count}`);
    }
  } else { console.log("  ERRORE:", pp(wallQuant.error)); }

  // 7. Quantità materiali per pavimenti
  console.log("\n── 7. get_material_quantities (OST_Floors) ──");
  const floorQuant = await sendCommand("get_material_quantities", { categoryFilters: ["OST_Floors"], maxResults: 10 });
  if (floorQuant.ok) {
    console.log(`  Totale materiali: ${floorQuant.result.totalMaterials}, count: ${floorQuant.result.totalCount}`);
    for (const m of (floorQuant.result.materials || floorQuant.result.items || []).slice(0, 5)) {
      console.log(`    ${m.name}: area=${m.area?.toFixed(2)}, vol=${m.volume?.toFixed(2)}, count=${m.count}`);
    }
  } else { console.log("  ERRORE:", pp(floorQuant.error)); }

  // 8. Parametri di un muro (per vedere i dati di tipo/stratigrafia)
  console.log("\n── 8. get_element_parameters (wall - type params) ──");
  const walls = await sendCommand("ai_element_filter", {
    data: { filterCategory: "OST_Walls", includeInstances: true, maxElements: 1 }
  });
  if (walls.ok && walls.result.elements.length > 0) {
    const wallId = walls.result.elements[0].elementId;
    console.log(`  Muro ID: ${wallId} - ${walls.result.elements[0].name}`);
    const params = await sendCommand("get_element_parameters", { elementIds: [wallId] });
    if (params.ok) {
      const el = params.result.elements[0];
      // Show type parameters (prefixed with [Type])
      const typeParams = el.parameters.filter(p => p.name.startsWith("[Type]") || p.group === "Type" || p.isType);
      console.log(`  Parametri totali: ${el.parameters.length}, Type params: ${typeParams.length}`);
      // Show structure-related params
      const structureParams = el.parameters.filter(p =>
        /structure|layer|width|thick|function|material/i.test(p.name) || /structure|layer|width|thick|function|material/i.test(p.value || "")
      );
      if (structureParams.length > 0) {
        console.log(`  Parametri stratigrafia/materiale:`);
        for (const p of structureParams) {
          console.log(`    ${p.name} = ${p.value}`);
        }
      }
      // Show ALL type params
      console.log(`  Tutti i Type params:`);
      for (const p of typeParams.slice(0, 20)) {
        console.log(`    ${p.name} = ${p.value}`);
      }
    } else { console.log("  ERRORE params:", pp(params.error)); }
  }

  // 9. send_code_to_revit per ottenere la stratigrafia di un muro
  console.log("\n── 9. send_code_to_revit (wall compound structure) ──");
  if (walls.ok && walls.result.elements.length > 0) {
    const wallId = walls.result.elements[0].elementId;
    const code = `
var wall = document.GetElement(new ElementId(${wallId})) as Autodesk.Revit.DB.Wall;
if (wall == null) return "Not a wall";
var wallType = wall.WallType;
var cs = wallType.GetCompoundStructure();
if (cs == null) return "No compound structure (curtain/stacked wall?)";
var layers = new System.Collections.Generic.List<object>();
for (int i = 0; i < cs.LayerCount; i++) {
    var layer = cs.GetLayers()[i];
    var matId = layer.MaterialId;
    var matName = matId != ElementId.InvalidElement
        ? document.GetElement(matId)?.Name ?? "(null)"
        : "(none)";
    layers.Add(new {
        index = i,
        function_ = layer.Function.ToString(),
        width_mm = Math.Round(layer.Width * 304.8, 2),
        materialId = matId.Value,
        materialName = matName
    });
}
return new { wallType = wallType.Name, totalWidth_mm = Math.Round(cs.GetWidth() * 304.8, 2), layerCount = cs.LayerCount, layers = layers };
`;
    const codeRes = await sendCommand("send_code_to_revit", { code: code.trim() });
    if (codeRes.ok) { console.log(`  ${pp(codeRes.result)}`); }
    else { console.log("  ERRORE:", pp(codeRes.error)); }
  }

  console.log("\n=== FINE TEST MATERIALI ===");
}

run().catch(e => console.error("FATAL:", e.message));
