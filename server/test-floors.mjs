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

// ── Materiali necessari ──
const materialsNeeded = [
  { name: "Gres Florim Airtech Basel Grey 60x120", materialClass: "Ceramic", color: "#B0A89A" },
  { name: "Malta adesiva", materialClass: "Miscellaneous", color: "#C8C0B8" },
  { name: "Imp. malta bicomponente Volteco Plastivo 180", materialClass: "Miscellaneous", color: "#A0A0A0" },
  { name: "Massetto BS40HR Retanol Indestra", materialClass: "Concrete", color: "#9A9A93" },
  { name: "Isolante anticalpestio Isofmat Special", materialClass: "Insulation", color: "#FFD700" },
  { name: "Riempimento leggero Boden Service BS-S", materialClass: "Concrete", color: "#C0B8A0" },
  { name: "Tappeto tecnico Emco Marshall", materialClass: "Miscellaneous", color: "#505050" },
  { name: "Massetto Retanol Indestra 100", materialClass: "Concrete", color: "#9A9A93" },
  { name: "Terrazzo alla veneziana SB Extreme Style", materialClass: "Stone", color: "#D8CFC0" },
  { name: "Massetto Retanol Xtreme Pro", materialClass: "Concrete", color: "#9A9A93" },
  { name: "Isolante anticalpestio Mapei Mapesilent Roll", materialClass: "Insulation", color: "#FFD700" },
  { name: "Barriera al vapore", materialClass: "Membrane", color: "#2020A0" },
  { name: "Coibentazione Isover TDPT 50", materialClass: "Insulation", color: "#FFCC00" },
];

async function run() {
  console.log("=== CREAZIONE PAVIMENTI DA PDF ===\n");

  // Step 1: Check existing materials
  console.log("── 1. Verifica materiali esistenti ──");
  const existing = await sendCommand("get_materials", {});
  if (!existing.ok) { console.log("ERRORE get_materials:", JSON.stringify(existing.error)); return; }
  console.log(`  ${existing.result.materialCount} materiali esistenti nel progetto`);

  // Step 2: Create missing materials
  console.log("\n── 2. Creazione materiali mancanti ──");
  const materialIds = {};

  for (const mat of materialsNeeded) {
    const found = existing.result.materials.find(m => m.name.toLowerCase() === mat.name.toLowerCase());
    if (found) {
      materialIds[mat.name] = found.id;
      console.log(`  ✓ ${mat.name} — già presente (ID: ${found.id})`);
      continue;
    }

    const res = await sendCommand("create_material", mat);
    if (res.ok) {
      materialIds[mat.name] = res.result.materialId;
      console.log(`  + ${mat.name} — creato (ID: ${res.result.materialId})`);
    } else {
      console.log(`  ✗ ${mat.name} — ERRORE: ${JSON.stringify(res.error)}`);
    }
  }

  console.log(`\n  Materiali pronti: ${Object.keys(materialIds).length}/${materialsNeeded.length}`);

  // Step 3: Get existing floor types to find a base FloorType
  console.log("\n── 3. Ricerca tipo pavimento base ──");
  const types = await sendCommand("get_available_family_types", { categoryList: ["OST_Floors"] });
  if (!types.ok) { console.log("ERRORE:", JSON.stringify(types.error)); return; }
  console.log(`  ${types.result.length} tipi pavimento disponibili`);
  for (const t of types.result.slice(0, 5)) {
    console.log(`    [${t.familyTypeId}] ${t.familyName}: ${t.typeName}`);
  }

  // Find a floor type with compound structure (skip curtain panels etc)
  let baseTypeId = null;
  let baseTypeName = null;
  for (const t of types.result) {
    const cs = await sendCommand("get_compound_structure", { typeId: t.familyTypeId });
    if (cs.ok && cs.result.hasCompoundStructure) {
      baseTypeId = t.familyTypeId;
      baseTypeName = t.typeName;
      console.log(`\n  Base con compound structure: [${baseTypeId}] ${baseTypeName}`);
      console.log(`    Layers: ${cs.result.layerCount}, spessore: ${cs.result.totalWidthMm}mm`);
      break;
    }
  }

  if (!baseTypeId) {
    console.log("  ERRORE: nessun tipo pavimento con compound structure trovato!");
    return;
  }

  // Step 4: Create 4 floor types
  console.log("\n── 4. Creazione 4 tipi pavimento ──");

  const floorDefs = [
    {
      name: "FB-M 01b-v5",
      layers: [
        { function: "Finish1", widthMm: 9, materialName: "Gres Florim Airtech Basel Grey 60x120" },
        { function: "Finish1", widthMm: 6, materialName: "Malta adesiva" },
        { function: "Membrane", widthMm: 0.1, materialName: "Imp. malta bicomponente Volteco Plastivo 180" },
        { function: "Substrate", widthMm: 55, materialName: "Massetto BS40HR Retanol Indestra" },
        { function: "Insulation", widthMm: 5, materialName: "Isolante anticalpestio Isofmat Special" },
        { function: "Substrate", widthMm: 55, materialName: "Riempimento leggero Boden Service BS-S" },
        { function: "Structure", widthMm: 1 },
      ]
    },
    {
      name: "FB-M 01c-v1",
      layers: [
        { function: "Finish1", widthMm: 25, materialName: "Tappeto tecnico Emco Marshall" },
        { function: "Substrate", widthMm: 50, materialName: "Massetto Retanol Indestra 100" },
        { function: "Substrate", widthMm: 55, materialName: "Riempimento leggero Boden Service BS-S" },
        { function: "Structure", widthMm: 1 },
      ]
    },
    {
      name: "FB-M 01d-v0",
      layers: [
        { function: "Finish1", widthMm: 15, materialName: "Terrazzo alla veneziana SB Extreme Style" },
        { function: "Substrate", widthMm: 60, materialName: "Massetto Retanol Xtreme Pro" },
        { function: "Insulation", widthMm: 5, materialName: "Isolante anticalpestio Mapei Mapesilent Roll" },
        { function: "Membrane", widthMm: 0.1, materialName: "Barriera al vapore" },
        { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TDPT 50" },
        { function: "Structure", widthMm: 1 },
      ]
    },
    {
      name: "FB-M 01d-v1",
      layers: [
        { function: "Finish1", widthMm: 25, materialName: "Tappeto tecnico Emco Marshall" },
        { function: "Substrate", widthMm: 50, materialName: "Massetto Retanol Xtreme Pro" },
        { function: "Insulation", widthMm: 5, materialName: "Isolante anticalpestio Mapei Mapesilent Roll" },
        { function: "Membrane", widthMm: 0.1, materialName: "Barriera al vapore" },
        { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TDPT 50" },
        { function: "Structure", widthMm: 1 },
      ]
    }
  ];

  for (const def of floorDefs) {
    console.log(`\n  >> ${def.name}`);

    // Duplicate from base type using the new synchronous tool
    const dupRes = await sendCommand("duplicate_system_type", {
      sourceTypeId: baseTypeId,
      category: "OST_Floors",
      newName: def.name
    });

    if (!dupRes.ok) {
      console.log(`     ✗ Duplicazione fallita: ${JSON.stringify(dupRes.error)}`);
      continue;
    }

    const newTypeId = dupRes.result.typeId;
    const existed = dupRes.result.alreadyExisted;
    console.log(`     ✓ Tipo ${existed ? "già esistente" : "creato"}: ID ${newTypeId}`);

    // Set compound structure with our layers
    const setRes = await sendCommand("set_compound_structure", {
      typeId: newTypeId,
      action: "replace",
      dryRun: false,
      layers: def.layers
    });

    if (setRes.ok) {
      console.log(`     ✓ Stratigrafia impostata: ${setRes.result.layerCount} layers, ${setRes.result.totalWidthMm}mm`);
    } else {
      console.log(`     ✗ Set stratigrafia fallita: ${JSON.stringify(setRes.error)}`);
    }
  }

  // Verify
  console.log("\n── 5. Verifica finale ──");
  for (const def of floorDefs) {
    const verify = await sendCommand("get_compound_structure", { typeName: def.name, category: "OST_Floors" });
    if (verify.ok) {
      console.log(`\n  ${def.name}: ${verify.result.totalWidthMm}mm, ${verify.result.layerCount} layers`);
      for (const l of (verify.result.layers || [])) {
        console.log(`    [${l.index}] ${l.function_} — ${l.widthMm}mm — ${l.materialName}`);
      }
    } else {
      console.log(`  ${def.name}: NON TROVATO`);
    }
  }

  console.log("\n=== FINE ===");
}

run().catch(e => console.error("FATAL:", e.message));
