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

async function run() {
  console.log("=== TEST MURI — DIAGNOSTICA ERRORI ===\n");

  // Find a base wall type
  console.log("── 1. Ricerca tipo muro base ──");
  const types = await sendCommand("get_available_family_types", { categoryList: ["OST_Walls"] });
  if (!types.ok) { console.log("ERRORE:", JSON.stringify(types.error)); return; }
  console.log(`  ${types.result.length} tipi muro disponibili`);

  let baseTypeId = null;
  for (const t of types.result.slice(0, 10)) {
    const cs = await sendCommand("get_compound_structure", { typeId: t.familyTypeId });
    if (cs.ok && cs.result.hasCompoundStructure) {
      baseTypeId = t.familyTypeId;
      console.log(`  Base: [${baseTypeId}] ${t.typeName} (${cs.result.layerCount} layers, ${cs.result.totalWidthMm}mm)`);
      for (const l of cs.result.layers) {
        console.log(`    [${l.index}] ${l.function_} — ${l.widthMm}mm — ${l.materialName}`);
      }
      break;
    }
  }
  if (!baseTypeId) { console.log("  Nessun muro con compound structure!"); return; }

  // Test 1: Correct wall — should succeed
  console.log("\n── 2. Test CORRETTO — muro valido ──");
  const dup1 = await sendCommand("duplicate_system_type", { sourceTypeId: baseTypeId, category: "OST_Walls", newName: "Test-Muro-OK" });
  if (dup1.ok) {
    const set1 = await sendCommand("set_compound_structure", {
      typeId: dup1.result.typeId,
      action: "replace",
      dryRun: false,
      layers: [
        { function: "Finish1", widthMm: 15, materialName: "Default Wall" },
        { function: "Structure", widthMm: 200 },
        { function: "Finish2", widthMm: 15, materialName: "Default Wall" },
      ]
    });
    console.log(`  ${set1.ok ? "✓ OK" : "✗ ERRORE"}: ${JSON.stringify(set1.ok ? set1.result : set1.error, null, 2)}`);
  }

  // Test 2: Membrane with thickness — should show MembraneTooThick
  console.log("\n── 3. Test ERRORE — Membrane con spessore ──");
  const dup2 = await sendCommand("duplicate_system_type", { sourceTypeId: baseTypeId, category: "OST_Walls", newName: "Test-Muro-Err1" });
  if (dup2.ok) {
    const set2 = await sendCommand("set_compound_structure", {
      typeId: dup2.result.typeId,
      action: "replace",
      dryRun: false,
      layers: [
        { function: "Finish1", widthMm: 15 },
        { function: "Substrate", widthMm: 30 },
        { function: "Structure", widthMm: 200 },
        { function: "Finish2", widthMm: 15 },
      ]
    });
    console.log(`  ${set2.ok ? "✓ OK" : "✗ ERRORE"}:`);
    console.log(JSON.stringify(set2.ok ? set2.result : set2.error, null, 2));
  }

  // Test 3: Wrong layer order (Structure before Finish1)
  console.log("\n── 4. Test ERRORE — ordine layers sbagliato ──");
  const dup3 = await sendCommand("duplicate_system_type", { sourceTypeId: baseTypeId, category: "OST_Walls", newName: "Test-Muro-Err2" });
  if (dup3.ok) {
    const set3 = await sendCommand("set_compound_structure", {
      typeId: dup3.result.typeId,
      action: "replace",
      dryRun: false,
      layers: [
        { function: "Structure", widthMm: 200 },
        { function: "Finish1", widthMm: 15 },
        { function: "Substrate", widthMm: 30 },
      ]
    });
    console.log(`  ${set3.ok ? "✓ OK" : "✗ ERRORE"}:`);
    console.log(JSON.stringify(set3.ok ? set3.result : set3.error, null, 2));
  }

  // Test 4: Non-membrane layer with 0 width
  console.log("\n── 5. Test ERRORE — layer con spessore 0 ──");
  const dup4 = await sendCommand("duplicate_system_type", { sourceTypeId: baseTypeId, category: "OST_Walls", newName: "Test-Muro-Err3" });
  if (dup4.ok) {
    const set4 = await sendCommand("set_compound_structure", {
      typeId: dup4.result.typeId,
      action: "replace",
      dryRun: false,
      layers: [
        { function: "Finish1", widthMm: 0 },
        { function: "Structure", widthMm: 200 },
        { function: "Finish2", widthMm: 15 },
      ]
    });
    console.log(`  ${set4.ok ? "✓ OK" : "✗ ERRORE"}:`);
    console.log(JSON.stringify(set4.ok ? set4.result : set4.error, null, 2));
  }

  // Test 5: Very thin layer (below minimum)
  console.log("\n── 6. Test ERRORE — layer troppo sottile ──");
  const dup5 = await sendCommand("duplicate_system_type", { sourceTypeId: baseTypeId, category: "OST_Walls", newName: "Test-Muro-Err4" });
  if (dup5.ok) {
    const set5 = await sendCommand("set_compound_structure", {
      typeId: dup5.result.typeId,
      action: "replace",
      dryRun: false,
      layers: [
        { function: "Finish1", widthMm: 0.01 },
        { function: "Structure", widthMm: 200 },
        { function: "Finish2", widthMm: 15 },
      ]
    });
    console.log(`  ${set5.ok ? "✓ OK" : "✗ ERRORE"}:`);
    console.log(JSON.stringify(set5.ok ? set5.result : set5.error, null, 2));
  }

  console.log("\n=== FINE ===");
}

run().catch(e => console.error("FATAL:", e.message));
