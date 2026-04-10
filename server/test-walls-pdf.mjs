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

// ── Materiali pareti cartongesso ──
const materialsNeeded = [
  { name: "Lastra cartongesso standard GKB", materialClass: "Gypsum", color: "#E8E0D0" },
  { name: "Lastra cartongesso idrofugo GKI", materialClass: "Gypsum", color: "#A8D8A8" },
  { name: "Lastra cartongesso ignifuga Diamant", materialClass: "Gypsum", color: "#D8A8B8" },
  { name: "Struttura parete cartongesso", materialClass: "Metal", color: "#C0C0C0" },
  { name: "Coibentazione Isover TW-KF", materialClass: "Insulation", color: "#FFD700" },
  { name: "Intercapedine aria", materialClass: "Miscellaneous", color: "#E0F0FF" },
];

// ── 6 tipi parete dal PDF "Pacchetti Verticali" ──
// Tutte 12.50 cm totali: board+board + stud 7.5cm (5cm insul + 2.5cm air) + board+board
// Varianti nel tipo di lastra esterna/interna:
//   GKB = standard, GKI = idrofugo, DM = Diamant ignifugo

const wallDefs = [
  {
    name: "IW-01",
    description: "Parete interna isolata — standard",
    epu: "NP.04.05.02.02.e1a",
    layers: [
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TW-KF" },
      { function: "Structure", widthMm: 25, materialName: "Intercapedine aria" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
    ]
  },
  {
    name: "IW-01-IDRO",
    description: "Parete interna isolata — idrofugo su lati esterni",
    epu: "NP.04.05.02.02.e1b",
    layers: [
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso idrofugo GKI" },
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TW-KF" },
      { function: "Structure", widthMm: 25, materialName: "Intercapedine aria" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso idrofugo GKI" },
    ]
  },
  {
    name: "IW-01-DM",
    description: "Parete interna isolata — Diamant ignifugo su lati esterni",
    epu: "NP.04.05.02.02.e1d",
    layers: [
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso ignifuga Diamant" },
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TW-KF" },
      { function: "Structure", widthMm: 25, materialName: "Intercapedine aria" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso ignifuga Diamant" },
    ]
  },
  {
    name: "IW-01-IDRO+DM",
    description: "Parete interna isolata — idrofugo + Diamant su lati esterni",
    epu: "NP.04.05.02.02.e1e",
    layers: [
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso idrofugo GKI" },
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TW-KF" },
      { function: "Structure", widthMm: 25, materialName: "Intercapedine aria" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso ignifuga Diamant" },
    ]
  },
  {
    name: "IW-01-IDRO+GKB",
    description: "Parete interna isolata — idrofugo un lato, standard altro",
    epu: "NP.04.05.02.02.e1c",
    layers: [
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso idrofugo GKI" },
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TW-KF" },
      { function: "Structure", widthMm: 25, materialName: "Intercapedine aria" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
    ]
  },
  {
    name: "IW-01-DM+GKB",
    description: "Parete interna isolata — Diamant un lato, standard altro",
    epu: "NP.04.05.02.02.e1c",
    layers: [
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso ignifuga Diamant" },
      { function: "Finish1", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Insulation", widthMm: 50, materialName: "Coibentazione Isover TW-KF" },
      { function: "Structure", widthMm: 25, materialName: "Intercapedine aria" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
      { function: "Finish2", widthMm: 12.5, materialName: "Lastra cartongesso standard GKB" },
    ]
  },
];

async function run() {
  console.log("=== CREAZIONE PARETI DA PDF — Pacchetti Verticali ===\n");

  // Step 1: Materials
  console.log("── 1. Creazione materiali ──");
  const existing = await sendCommand("get_materials", {});
  if (!existing.ok) { console.log("ERRORE:", JSON.stringify(existing.error)); return; }
  const existingNames = new Set(existing.result.materials.map(m => m.name.toLowerCase()));

  for (const mat of materialsNeeded) {
    if (existingNames.has(mat.name.toLowerCase())) {
      console.log(`  ✓ ${mat.name} — già presente`);
      continue;
    }
    const res = await sendCommand("create_material", mat);
    console.log(`  ${res.ok ? "+" : "✗"} ${mat.name} — ${res.ok ? "creato" : JSON.stringify(res.error)}`);
  }

  // Step 2: Find base wall type
  console.log("\n── 2. Ricerca tipo muro base ──");
  const types = await sendCommand("get_available_family_types", { categoryList: ["OST_Walls"] });
  if (!types.ok) { console.log("ERRORE:", JSON.stringify(types.error)); return; }

  let baseTypeId = null;
  for (const t of types.result) {
    const cs = await sendCommand("get_compound_structure", { typeId: t.familyTypeId });
    if (cs.ok && cs.result.hasCompoundStructure) {
      baseTypeId = t.familyTypeId;
      console.log(`  Base: [${baseTypeId}] ${t.typeName}`);
      break;
    }
  }
  if (!baseTypeId) { console.log("  Nessun muro con compound structure!"); return; }

  // Step 3: Create 6 wall types
  console.log("\n── 3. Creazione 6 tipi parete ──");

  for (const def of wallDefs) {
    console.log(`\n  >> ${def.name} — ${def.description}`);

    const dupRes = await sendCommand("duplicate_system_type", {
      sourceTypeId: baseTypeId,
      category: "OST_Walls",
      newName: def.name
    });

    if (!dupRes.ok) {
      console.log(`     ✗ Duplicazione: ${JSON.stringify(dupRes.error)}`);
      continue;
    }
    console.log(`     ✓ Tipo ${dupRes.result.alreadyExisted ? "esistente" : "creato"}: ID ${dupRes.result.typeId}`);

    const setRes = await sendCommand("set_compound_structure", {
      typeId: dupRes.result.typeId,
      action: "replace",
      dryRun: false,
      layers: def.layers
    });

    if (setRes.ok) {
      console.log(`     ✓ Stratigrafia: ${setRes.result.layerCount} layers, ${setRes.result.totalWidthMm}mm`);
    } else {
      // Show diagnostic errors
      try {
        const errObj = JSON.parse(setRes.error.message);
        console.log(`     ✗ ${errObj.message}`);
        if (errObj.suggestion) console.log(`       FIX: ${errObj.suggestion}`);
      } catch {
        console.log(`     ✗ ${JSON.stringify(setRes.error)}`);
      }
    }
  }

  // Step 4: Verify
  console.log("\n── 4. Verifica finale ──");
  for (const def of wallDefs) {
    const v = await sendCommand("get_compound_structure", { typeName: def.name, category: "OST_Walls" });
    if (v.ok) {
      console.log(`\n  ${def.name}: ${v.result.totalWidthMm}mm, ${v.result.layerCount} layers`);
      for (const l of (v.result.layers || []))
        console.log(`    [${l.index}] ${l.function_} — ${l.widthMm}mm — ${l.materialName}`);
    } else {
      console.log(`  ${def.name}: NON TROVATO`);
    }
  }

  console.log("\n=== FINE ===");
}

run().catch(e => console.error("FATAL:", e.message));
