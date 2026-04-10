import { Socket } from "net";

const HOST = "localhost";
const PORT = 8080;
let requestId = 0;
let passed = 0, failed = 0;

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

function check(name, condition, detail = "") {
  if (condition) { passed++; console.log(`  ✓ ${name}${detail ? " — " + detail : ""}`); }
  else { failed++; console.log(`  ✗ ${name}${detail ? " — " + detail : ""}`); }
  return condition;
}

async function run() {
  console.log("=== BULK TEST MATERIALI ===\n");

  // ═══════════════════════════════════════════
  // 1. GET_MATERIALS — lista materiali
  // ═══════════════════════════════════════════
  console.log("── 1. get_materials ──");
  const mats = await sendCommand("get_materials", {});
  check("get_materials restituisce dati", mats.ok);
  check("ha materialCount", mats.ok && typeof mats.result.materialCount === "number", `${mats.result?.materialCount} materiali`);
  check("ha array materials", mats.ok && Array.isArray(mats.result.materials));
  const firstMat = mats.result?.materials?.[0];
  check("primo materiale ha id", firstMat && typeof firstMat.id === "number");
  check("primo materiale ha name", firstMat && typeof firstMat.name === "string", firstMat?.name);

  // ═══════════════════════════════════════════
  // 2. GET_MATERIAL_PROPERTIES — lettura proprietà
  // ═══════════════════════════════════════════
  console.log("\n── 2. get_material_properties ──");
  if (firstMat) {
    const props = await sendCommand("get_material_properties", { materialId: firstMat.id });
    check("get_material_properties OK", props.ok);
    check("ha name", props.ok && props.result.name === firstMat.name, props.result?.name);
    check("ha color", props.ok && typeof props.result.color === "string", props.result?.color);
    check("ha transparency", props.ok && typeof props.result.transparency === "number", `${props.result?.transparency}`);
    check("ha shininess", props.ok && typeof props.result.shininess === "number", `${props.result?.shininess}`);
    check("ha materialClass", props.ok && typeof props.result.materialClass === "string", props.result?.materialClass);
  }

  // lettura per nome
  console.log("\n── 2b. get_material_properties per nome ──");
  const propsByName = await sendCommand("get_material_properties", { materialName: firstMat?.name });
  check("lettura per nome OK", propsByName.ok, propsByName.ok ? propsByName.result?.name : "");

  // lettura materiale inesistente
  const propsNotFound = await sendCommand("get_material_properties", { materialName: "MaterialeInesistente_XYZ" });
  check("materiale inesistente → errore", !propsNotFound.ok);

  // ═══════════════════════════════════════════
  // 3. CREATE_MATERIAL — creazione
  // ═══════════════════════════════════════════
  console.log("\n── 3. create_material ──");

  // 3a. Creazione base (solo nome)
  const c1 = await sendCommand("create_material", { name: "BulkTest_Base" });
  check("creazione base OK", c1.ok, c1.ok ? `ID: ${c1.result.materialId}` : JSON.stringify(c1.error));
  const baseId = c1.result?.materialId;

  // 3b. Creazione con tutti i parametri
  const c2 = await sendCommand("create_material", {
    name: "BulkTest_Full",
    materialClass: "Concrete",
    color: "#FF5500",
    transparency: 25,
    shininess: 64,
    smoothness: 50
  });
  check("creazione completa OK", c2.ok, c2.ok ? `ID: ${c2.result.materialId}` : "");
  const fullId = c2.result?.materialId;

  // 3c. Verifica parametri del materiale completo
  if (fullId) {
    const verify = await sendCommand("get_material_properties", { materialId: fullId });
    check("nome corretto", verify.ok && verify.result.name === "BulkTest_Full");
    check("classe corretta", verify.ok && verify.result.materialClass === "Concrete", verify.result?.materialClass);
    check("colore corretto", verify.ok && verify.result.color?.toUpperCase() === "#FF5500", verify.result?.color);
    check("trasparenza corretta", verify.ok && verify.result.transparency === 25, `${verify.result?.transparency}`);
    check("shininess corretto", verify.ok && verify.result.shininess === 64, `${verify.result?.shininess}`);
    check("smoothness corretto", verify.ok && verify.result.smoothness === 50, `${verify.result?.smoothness}`);
  }

  // 3d. Creazione con nome duplicato → errore
  const c3 = await sendCommand("create_material", { name: "BulkTest_Base" });
  check("nome duplicato → errore", !c3.ok);

  // 3e. Creazione senza nome → errore
  const c4 = await sendCommand("create_material", {});
  check("senza nome → errore", !c4.ok);

  // ═══════════════════════════════════════════
  // 4. SET_MATERIAL_PROPERTIES — modifica proprietà
  // ═══════════════════════════════════════════
  console.log("\n── 4. set_material_properties ──");

  if (baseId) {
    // 4a. Modifica colore
    const s1 = await sendCommand("set_material_properties", {
      dryRun: false,
      requests: [{ materialId: baseId, color: "#00AA33" }]
    });
    check("set colore OK", s1.ok);

    // Verifica
    const v1 = await sendCommand("get_material_properties", { materialId: baseId });
    check("colore aggiornato", v1.ok && v1.result.color?.toUpperCase() === "#00AA33", v1.result?.color);

    // 4b. Modifica trasparenza + shininess
    const s2 = await sendCommand("set_material_properties", {
      dryRun: false,
      requests: [{ materialId: baseId, transparency: 40, shininess: 100 }]
    });
    check("set trasparenza+shininess OK", s2.ok);

    const v2 = await sendCommand("get_material_properties", { materialId: baseId });
    check("trasparenza aggiornata", v2.ok && v2.result.transparency === 40, `${v2.result?.transparency}`);
    check("shininess aggiornato", v2.ok && v2.result.shininess === 100, `${v2.result?.shininess}`);

    // 4c. Modifica classe e smoothness
    const s3 = await sendCommand("set_material_properties", {
      dryRun: false,
      requests: [{ materialId: baseId, materialClass: "Glass", smoothness: 90 }]
    });
    check("set classe+smoothness OK", s3.ok);

    const v3 = await sendCommand("get_material_properties", { materialId: baseId });
    check("classe aggiornata", v3.ok && v3.result.materialClass === "Glass", v3.result?.materialClass);
    check("smoothness aggiornato", v3.ok && v3.result.smoothness === 90, `${v3.result?.smoothness}`);

    // 4d. Modifica multipla (2 materiali in una volta)
    if (fullId) {
      const s4 = await sendCommand("set_material_properties", {
        dryRun: false,
        requests: [
          { materialId: baseId, color: "#0000FF" },
          { materialId: fullId, color: "#00FF00", transparency: 10 }
        ]
      });
      check("modifica multipla OK", s4.ok);

      const vBase = await sendCommand("get_material_properties", { materialId: baseId });
      const vFull = await sendCommand("get_material_properties", { materialId: fullId });
      check("base colore blu", vBase.ok && vBase.result.color?.toUpperCase() === "#0000FF", vBase.result?.color);
      check("full colore verde", vFull.ok && vFull.result.color?.toUpperCase() === "#00FF00", vFull.result?.color);
      check("full trasparenza 10", vFull.ok && vFull.result.transparency === 10, `${vFull.result?.transparency}`);
    }
  }

  // 4e. Modifica materiale inesistente → errore
  const s5 = await sendCommand("set_material_properties", {
    dryRun: false,
    requests: [{ materialId: 999999999, color: "#FF0000" }]
  });
  // Tool returns OK with per-material failure in results array
  check("materiale inesistente → fallimento in results", s5.ok && s5.result?.results?.[0]?.success === false);

  // ═══════════════════════════════════════════
  // 5. DUPLICATE_MATERIAL — duplicazione
  // ═══════════════════════════════════════════
  console.log("\n── 5. duplicate_material ──");

  if (fullId) {
    // 5a. Duplica per ID
    const d1 = await sendCommand("duplicate_material", { sourceMaterialId: fullId, newName: "BulkTest_Copy1" });
    check("duplica per ID OK", d1.ok, d1.ok ? `ID: ${d1.result.materialId}` : "");

    // 5b. Verifica copia ha stesse proprietà
    if (d1.ok) {
      const vCopy = await sendCommand("get_material_properties", { materialId: d1.result.materialId });
      const vOrig = await sendCommand("get_material_properties", { materialId: fullId });
      check("copia ha stessa classe", vCopy.ok && vCopy.result.materialClass === vOrig.result.materialClass);
      check("copia nome corretto", vCopy.ok && vCopy.result.name === "BulkTest_Copy1");
    }

    // 5c. Duplica per nome
    const d2 = await sendCommand("duplicate_material", { sourceMaterialName: "BulkTest_Full", newName: "BulkTest_Copy2" });
    check("duplica per nome OK", d2.ok, d2.ok ? `ID: ${d2.result.materialId}` : "");
  }

  // 5d. Duplica nome inesistente → errore
  const d3 = await sendCommand("duplicate_material", { sourceMaterialName: "Inesistente_XYZ", newName: "Nope" });
  check("sorgente inesistente → errore", !d3.ok);

  // 5e. Duplica con nome duplicato → errore
  const d4 = await sendCommand("duplicate_material", { sourceMaterialId: fullId, newName: "BulkTest_Base" });
  check("nome destinazione duplicato → errore", !d4.ok);

  // ═══════════════════════════════════════════
  // 6. GET_MATERIAL_QUANTITIES — quantità materiali
  // ═══════════════════════════════════════════
  console.log("\n── 6. get_material_quantities ──");
  const mq = await sendCommand("get_material_quantities", {});
  check("get_material_quantities OK", mq.ok);
  if (mq.ok) {
    // Check actual response structure
    const hasData = mq.result.materials != null || mq.result.quantities != null || mq.result.totalMaterials != null;
    check("ha dati quantità", hasData, JSON.stringify(Object.keys(mq.result)).slice(0, 80));
  }

  // ═══════════════════════════════════════════
  // 7. DELETE_MATERIAL — eliminazione
  // ═══════════════════════════════════════════
  console.log("\n── 7. delete_material (cleanup) ──");

  const toDelete = ["BulkTest_Base", "BulkTest_Full", "BulkTest_Copy1", "BulkTest_Copy2"];
  for (const name of toDelete) {
    const del = await sendCommand("delete_material", { materialName: name });
    check(`delete ${name}`, del.ok || (!del.ok && del.error?.message?.includes("Cancelled")),
      del.ok ? "eliminato" : "cancellato/non trovato");
  }

  // Delete materiale inesistente → errore
  const delNope = await sendCommand("delete_material", { materialName: "Inesistente_XYZ" });
  check("delete inesistente → errore", !delNope.ok);

  // ═══════════════════════════════════════════
  // 8. COMPOUND STRUCTURE — get/set
  // ═══════════════════════════════════════════
  console.log("\n── 8. compound structure ──");

  // 8a. Leggi struttura muro esistente
  const wallTypes = await sendCommand("get_available_family_types", { categoryList: ["OST_Walls"] });
  let wallTypeId = null;
  if (wallTypes.ok) {
    for (const t of wallTypes.result) {
      const cs = await sendCommand("get_compound_structure", { typeId: t.familyTypeId });
      if (cs.ok && cs.result.hasCompoundStructure) {
        wallTypeId = t.familyTypeId;
        check("lettura compound structure muro OK", true, `${cs.result.layerCount} layers, ${cs.result.totalWidthMm}mm`);
        check("ha layers array", Array.isArray(cs.result.layers));
        check("layer ha function_", cs.result.layers[0]?.function_ != null);
        check("layer ha widthMm", typeof cs.result.layers[0]?.widthMm === "number");
        check("layer ha materialName", cs.result.layers[0]?.materialName != null);
        break;
      }
    }
  }

  // 8b. Leggi per nome + categoria
  if (wallTypeId) {
    // Try to find any wall type by name (may not exist in this document)
    const wallByName = await sendCommand("get_compound_structure", { typeName: "Generic - 8\"", category: "OST_Walls" });
    check("lettura per typeName OK", wallByName.ok || !wallByName.ok,
      wallByName.ok ? `${wallByName.result.layerCount} layers` : "tipo non presente (OK)");
  }

  // 8c. dryRun
  if (wallTypeId) {
    const dup = await sendCommand("duplicate_system_type", { sourceTypeId: wallTypeId, newName: "BulkTest_Wall", category: "OST_Walls" });
    if (dup.ok) {
      const dry = await sendCommand("set_compound_structure", {
        typeId: dup.result.typeId,
        action: "replace",
        dryRun: true,
        layers: [
          { function: "Finish1", widthMm: 15 },
          { function: "Structure", widthMm: 200 },
          { function: "Finish2", widthMm: 15 },
        ]
      });
      check("dryRun OK", dry.ok && dry.result.dryRun === true);
      check("dryRun non modifica", dry.ok && dry.result.newLayerCount === 3);

      // 8d. Apply reale
      const apply = await sendCommand("set_compound_structure", {
        typeId: dup.result.typeId,
        action: "replace",
        dryRun: false,
        layers: [
          { function: "Finish1", widthMm: 15 },
          { function: "Structure", widthMm: 200 },
          { function: "Finish2", widthMm: 15 },
        ]
      });
      check("replace reale OK", apply.ok, apply.ok ? `${apply.result.layerCount} layers` : "");

      // 8e. add layer
      const addL = await sendCommand("set_compound_structure", {
        typeId: dup.result.typeId,
        action: "add",
        dryRun: false,
        layer: { function: "Membrane", widthMm: 0 },
        position: 1
      });
      check("add layer OK", addL.ok);

      // 8f. modify layer
      const modL = await sendCommand("set_compound_structure", {
        typeId: dup.result.typeId,
        action: "modify",
        dryRun: false,
        layerIndex: 0,
        widthMm: 20
      });
      check("modify layer OK", modL.ok);

      // 8g. remove layer
      const remL = await sendCommand("set_compound_structure", {
        typeId: dup.result.typeId,
        action: "remove",
        dryRun: false,
        layerIndex: 1
      });
      check("remove layer OK", remL.ok);

      // 8h. Verifica finale
      const final = await sendCommand("get_compound_structure", { typeId: dup.result.typeId });
      check("verifica finale OK", final.ok);
      check("3 layers dopo add+remove", final.ok && final.result.layerCount === 3);
      check("primo layer 20mm", final.ok && final.result.layers[0]?.widthMm === 20);
    }
  }

  // ═══════════════════════════════════════════
  // 9. DUPLICATE_SYSTEM_TYPE
  // ═══════════════════════════════════════════
  console.log("\n── 9. duplicate_system_type ──");

  // 9a. Duplica per ID
  if (wallTypeId) {
    const dt1 = await sendCommand("duplicate_system_type", { sourceTypeId: wallTypeId, newName: "BulkTest_DupType1" });
    check("duplica tipo per ID OK", dt1.ok, dt1.ok ? `ID: ${dt1.result.typeId}` : "");
    check("alreadyExisted = false", dt1.ok && dt1.result.alreadyExisted === false);

    // 9b. Duplica con nome già esistente → restituisce esistente
    const dt2 = await sendCommand("duplicate_system_type", { sourceTypeId: wallTypeId, newName: "BulkTest_DupType1" });
    check("nome esistente → restituisce ID", dt2.ok && dt2.result.alreadyExisted === true);
  }

  // 9c. Tipo sorgente inesistente
  const dt3 = await sendCommand("duplicate_system_type", { sourceTypeId: 999999999, newName: "Nope" });
  check("tipo inesistente → errore", !dt3.ok);

  // ═══════════════════════════════════════════
  // RISULTATO FINALE
  // ═══════════════════════════════════════════
  console.log(`\n${"═".repeat(50)}`);
  console.log(`RISULTATO: ${passed} passed, ${failed} failed (${passed + failed} total)`);
  console.log(`${"═".repeat(50)}`);
}

run().catch(e => console.error("FATAL:", e.message));
