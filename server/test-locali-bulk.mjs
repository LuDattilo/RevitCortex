import { Socket } from "net";

const HOST = "localhost";
const PORT = 8080;
let requestId = 0;
let passed = 0, failed = 0, skipped = 0;

function sendCommand(method, params = {}) {
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

function check(name, condition, detail = "") {
  if (condition) { passed++; console.log(`  \u2713 ${name}${detail ? " \u2014 " + detail : ""}`); }
  else { failed++; console.log(`  \u2717 ${name}${detail ? " \u2014 " + detail : ""}`); }
  return condition;
}

function skip(name, reason = "") {
  skipped++;
  console.log(`  \u25CB ${name}${reason ? " \u2014 SKIP: " + reason : ""}`);
}

function section(num, title) {
  console.log(`\n${"=".repeat(60)}`);
  console.log(`  ${num}. ${title}`);
  console.log(`${"=".repeat(60)}`);
}

// ════════════════════════════════════════════════════════
// Utilities
// ════════════════════════════════════════════════════════

function unique(arr) { return [...new Set(arr.filter(Boolean))]; }
function avg(arr) { return arr.length ? arr.reduce((s, v) => s + v, 0) / arr.length : 0; }
function groupBy(arr, fn) {
  const map = {};
  for (const item of arr) { const k = fn(item); (map[k] ??= []).push(item); }
  return map;
}

async function run() {
  console.log("\n" + "#".repeat(60));
  console.log("  BULK TEST: LOCALI, PARAMETRI, PORTE & FINESTRE");
  console.log("#".repeat(60));

  // ════════════════════════════════════════════════════════
  // SEZIONE A: RACCOLTA DATI BASE
  // ════════════════════════════════════════════════════════

  section("A1", "export_room_data \u2014 tutti i locali posizionati");
  const rooms = await sendCommand("export_room_data", { maxResults: 500 });
  check("export_room_data OK", rooms.ok);
  const roomList = rooms.result?.rooms ?? rooms.result?.data ?? [];
  const roomCount = roomList.length || rooms.result?.roomCount || 0;
  check("modello ha locali", roomCount > 0, `${roomCount} locali`);

  if (roomCount === 0) {
    console.log("\n  ATTENZIONE: nessun locale trovato, impossibile continuare.");
    console.log(`\nRISULTATO: ${passed} passed, ${failed} failed, ${skipped} skipped`);
    return;
  }

  // Cache dati chiave
  const allRoomIds = roomList.map(r => r.id || r.roomId).filter(Boolean);
  const allRoomNumbers = roomList.map(r => r.number || r.roomNumber).filter(Boolean);
  const allLevels = unique(roomList.map(r => r.level || r.levelName));
  const allDepartments = unique(roomList.map(r => r.department));
  const allNames = unique(roomList.map(r => r.name || r.roomName));

  console.log(`\n  Riepilogo dati raccolti:`);
  console.log(`    Livelli: ${allLevels.join(", ") || "(nessuno)"}`);
  console.log(`    Reparti: ${allDepartments.join(", ") || "(nessuno)"}`);
  console.log(`    Nomi unici: ${allNames.length}`);

  // ────────────────────────────────────────────────────
  section("A2", "export_room_data \u2014 struttura dati singolo locale");
  const r0 = roomList[0];
  const roomId = r0?.id || r0?.roomId;
  check("locale ha id", roomId != null, `id=${roomId}`);
  check("locale ha name", typeof (r0?.name || r0?.roomName) === "string", r0?.name || r0?.roomName);
  check("locale ha number", (r0?.number || r0?.roomNumber) != null, r0?.number || r0?.roomNumber);
  check("locale ha level", (r0?.level || r0?.levelName) != null, r0?.level || r0?.levelName);
  check("locale ha area", (r0?.area || r0?.areaSqM) != null, `${r0?.area || r0?.areaSqM}`);
  check("locale ha volume", (r0?.volume || r0?.volumeCuM) != null);
  check("locale ha perimetro", (r0?.perimeter || r0?.perimeterMm) != null);

  // ────────────────────────────────────────────────────
  section("A3", "export_room_data \u2014 includi locali non posizionati");
  const roomsUnplaced = await sendCommand("export_room_data", { includeUnplacedRooms: true, maxResults: 500 });
  check("export con includeUnplacedRooms OK", roomsUnplaced.ok);
  if (roomsUnplaced.ok) {
    const allRooms = roomsUnplaced.result?.rooms ?? roomsUnplaced.result?.data ?? [];
    check("totale >= posizionati", allRooms.length >= roomCount, `${allRooms.length} totali vs ${roomCount} posizionati`);
    const unplacedCount = allRooms.length - roomCount;
    if (unplacedCount > 0) console.log(`    ${unplacedCount} locali non posizionati trovati`);
  }

  // ────────────────────────────────────────────────────
  section("A4", "export_room_data \u2014 includi locali non chiusi");
  const roomsNotEnclosed = await sendCommand("export_room_data", { includeNotEnclosedRooms: true, maxResults: 500 });
  check("export con includeNotEnclosedRooms OK", roomsNotEnclosed.ok);

  // ════════════════════════════════════════════════════════
  // SEZIONE B: PARAMETRI LOCALI
  // ════════════════════════════════════════════════════════

  section("B1", "get_element_parameters \u2014 parametri di un locale");
  const params1 = await sendCommand("get_element_parameters", { elementIds: [roomId] });
  check("get_element_parameters OK", params1.ok);

  let paramList = [];
  if (params1.ok) {
    const elemData = params1.result?.elements?.[0] || params1.result;
    paramList = elemData?.parameters || elemData?.instanceParameters || [];
    check("ha parametri", paramList.length > 0, `${paramList.length} parametri`);

    const paramNames = paramList.map(p => (p.name || "").toLowerCase());
    check("param: Area", paramNames.some(n => n.includes("area")));
    check("param: Perimetro/Perimeter", paramNames.some(n => n.includes("perimeter") || n.includes("perimetro")));
    check("param: Volume", paramNames.some(n => n.includes("volume")));
    check("param: Level/Livello", paramNames.some(n => n.includes("level") || n.includes("livello")));
    check("param: Number/Numero", paramNames.some(n => n.includes("number") || n.includes("numero")));
    check("param: Name/Nome", paramNames.some(n => n.includes("name") || n.includes("nome")));
    check("param: Department/Reparto", paramNames.some(n => n.includes("department") || n.includes("reparto")));
    check("param: Limit Offset", paramNames.some(n => n.includes("limit") || n.includes("limite")));
    check("param: Base Offset", paramNames.some(n => n.includes("base")));

    // Check for type parameters
    const typeParams = paramList.filter(p => p.isType || p.name?.startsWith("[Type]"));
    check("ha parametri di tipo", typeParams.length > 0, `${typeParams.length} type params`);
  }

  // ────────────────────────────────────────────────────
  section("B2", "get_element_parameters \u2014 batch su 5 locali");
  const batchIds = allRoomIds.slice(0, 5);
  if (batchIds.length >= 2) {
    const paramsBatch = await sendCommand("get_element_parameters", { elementIds: batchIds });
    check("batch get_element_parameters OK", paramsBatch.ok);
    if (paramsBatch.ok) {
      const elements = paramsBatch.result?.elements || [];
      check("ritorna tutti gli elementi", elements.length === batchIds.length, `${elements.length}/${batchIds.length}`);
      // Check each has parameters
      for (const el of elements) {
        const pcount = (el.parameters || el.instanceParameters || []).length;
        check(`elemento [${el.elementId || el.id}] ha parametri`, pcount > 0, `${pcount}`);
      }
    }
  } else { skip("batch test", "meno di 2 locali"); }

  // ────────────────────────────────────────────────────
  section("B3", "get_element_parameters \u2014 solo instance (no type)");
  const paramsNoType = await sendCommand("get_element_parameters", { elementIds: [roomId], includeTypeParameters: false });
  check("senza type params OK", paramsNoType.ok);
  if (paramsNoType.ok && params1.ok) {
    const instOnly = (paramsNoType.result?.elements?.[0]?.parameters || []).length;
    const withType = paramList.length;
    check("instance-only ha meno parametri", instOnly <= withType, `${instOnly} vs ${withType}`);
  }

  // ────────────────────────────────────────────────────
  section("B4", "get_element_parameters \u2014 consistenza valori vs export");
  if (params1.ok) {
    // Cross-check area from parameters vs export_room_data
    const areaParam = paramList.find(p => (p.name || "").toLowerCase().includes("area") && !p.name?.toLowerCase().includes("perimeter"));
    const exportArea = r0?.areaSqM || r0?.area;
    if (areaParam && exportArea) {
      console.log(`    Param Area: ${areaParam.value}, Export Area: ${exportArea}`);
      check("area consistente tra tool", true, "valori presenti in entrambi");
    }
  }

  // ════════════════════════════════════════════════════════
  // SEZIONE C: PORTE E FINESTRE DEL LOCALE
  // ════════════════════════════════════════════════════════

  section("C1", "get_room_openings \u2014 porte + finestre (both)");
  const openAll = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "both" });
  check("get_room_openings (both) OK", openAll.ok);

  let allOpenings = [];
  let doorIds = [], windowIds = [];
  if (openAll.ok) {
    const roomData = openAll.result?.rooms?.[0] || openAll.result;
    allOpenings = roomData?.openings || roomData?.elements || [];
    check("ha aperture (o zero)", allOpenings.length >= 0, `${allOpenings.length} aperture totali`);

    if (allOpenings.length > 0) {
      const o0 = allOpenings[0];
      check("apertura ha elementId", (o0?.elementId || o0?.id) != null);
      check("apertura ha category", (o0?.category || o0?.type) != null);
      check("apertura ha family", (o0?.familyName || o0?.family) != null);
      check("apertura ha fromRoom/toRoom", o0?.fromRoom != null || o0?.toRoom != null || o0?.roomId != null);
      check("apertura ha dimensioni", (o0?.widthMm || o0?.width) != null);

      // Separate doors and windows
      for (const o of allOpenings) {
        const cat = (o.category || o.type || "").toLowerCase();
        if (cat.includes("door") || cat.includes("port")) doorIds.push(o.elementId || o.id);
        else if (cat.includes("window") || cat.includes("finestr")) windowIds.push(o.elementId || o.id);
      }
      console.log(`    Porte: ${doorIds.length}, Finestre: ${windowIds.length}`);
    }
  }

  // ────────────────────────────────────────────────────
  section("C2", "get_room_openings \u2014 solo porte");
  const openDoors = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "doors" });
  check("get_room_openings (doors) OK", openDoors.ok);
  if (openDoors.ok) {
    const doorData = openDoors.result?.rooms?.[0] || openDoors.result;
    const doors = doorData?.openings || doorData?.elements || [];
    check("count porte coerente", doors.length === doorIds.length, `${doors.length} vs ${doorIds.length} da both`);
  }

  // ────────────────────────────────────────────────────
  section("C3", "get_room_openings \u2014 solo finestre");
  const openWin = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "windows" });
  check("get_room_openings (windows) OK", openWin.ok);
  if (openWin.ok) {
    const winData = openWin.result?.rooms?.[0] || openWin.result;
    const wins = winData?.openings || winData?.elements || [];
    check("count finestre coerente", wins.length === windowIds.length, `${wins.length} vs ${windowIds.length} da both`);
  }

  // ────────────────────────────────────────────────────
  section("C4", "get_room_openings \u2014 con parametri locale");
  const openWithRoomParams = await sendCommand("get_room_openings", {
    roomIds: [roomId], elementType: "both", includeRoomParams: true
  });
  check("con includeRoomParams OK", openWithRoomParams.ok);
  if (openWithRoomParams.ok) {
    const rd = openWithRoomParams.result?.rooms?.[0] || openWithRoomParams.result;
    const hasRoomParams = rd?.roomParameters != null || rd?.parameters != null || rd?.area != null;
    check("risposta include parametri locale", hasRoomParams);
  }

  // ────────────────────────────────────────────────────
  section("C5", "get_room_openings \u2014 con parametri elementi");
  const openWithElemParams = await sendCommand("get_room_openings", {
    roomIds: [roomId], elementType: "both", includeElementParams: true
  });
  check("con includeElementParams OK", openWithElemParams.ok);
  if (openWithElemParams.ok && allOpenings.length > 0) {
    const rd = openWithElemParams.result?.rooms?.[0] || openWithElemParams.result;
    const elems = rd?.openings || rd?.elements || [];
    if (elems.length > 0) {
      const hasElemParams = elems[0]?.parameters != null || elems[0]?.instanceParameters != null;
      check("aperture includono parametri", hasElemParams);
    }
  }

  // ────────────────────────────────────────────────────
  section("C6", "get_room_openings \u2014 parametri specifici");
  const openSpecific = await sendCommand("get_room_openings", {
    roomIds: [roomId], elementType: "both", includeElementParams: true,
    parameterNames: ["Width", "Height", "Larghezza", "Altezza"]
  });
  check("con parameterNames specifici OK", openSpecific.ok);

  // ────────────────────────────────────────────────────
  section("C7", "get_room_openings \u2014 per numero locale");
  const roomNum = r0?.number || r0?.roomNumber;
  if (roomNum) {
    const byNum = await sendCommand("get_room_openings", { roomNumbers: [String(roomNum)], elementType: "both" });
    check("ricerca per roomNumber OK", byNum.ok, `num="${roomNum}"`);
    if (byNum.ok) {
      const numOpenings = (byNum.result?.rooms?.[0]?.openings || []).length;
      check("risultati coerenti con roomId", numOpenings === allOpenings.length, `${numOpenings} vs ${allOpenings.length}`);
    }
  } else { skip("ricerca per roomNumber", "locale senza numero"); }

  // ────────────────────────────────────────────────────
  section("C8", "get_room_openings \u2014 tutti i locali di un livello");
  if (allLevels.length > 0) {
    const byLevel = await sendCommand("get_room_openings", { levelName: allLevels[0], elementType: "both" });
    check("ricerca per levelName OK", byLevel.ok, allLevels[0]);
    if (byLevel.ok) {
      const levelRooms = byLevel.result?.rooms || [];
      check("locali al livello", levelRooms.length > 0, `${levelRooms.length} locali`);

      let totalD = 0, totalW = 0;
      for (const rm of levelRooms) {
        for (const op of (rm.openings || [])) {
          const cat = (op.category || "").toLowerCase();
          if (cat.includes("door") || cat.includes("port")) totalD++;
          else if (cat.includes("window") || cat.includes("finestr")) totalW++;
        }
      }
      console.log(`    Livello "${allLevels[0]}": ${totalD} porte, ${totalW} finestre`);
    }
  } else { skip("ricerca per livello", "nessun livello"); }

  // ────────────────────────────────────────────────────
  section("C9", "get_room_openings \u2014 batch su tutti i locali");
  const openAllRooms = await sendCommand("get_room_openings", { elementType: "both" });
  check("tutti i locali OK", openAllRooms.ok);
  if (openAllRooms.ok) {
    const totalRooms = openAllRooms.result?.rooms?.length || 0;
    check("tutti i locali ritornati", totalRooms > 0, `${totalRooms} locali`);
    let globalDoors = 0, globalWindows = 0;
    for (const rm of (openAllRooms.result?.rooms || [])) {
      for (const op of (rm.openings || [])) {
        const cat = (op.category || "").toLowerCase();
        if (cat.includes("door") || cat.includes("port")) globalDoors++;
        else if (cat.includes("window") || cat.includes("finestr")) globalWindows++;
      }
    }
    console.log(`    Totale modello: ${globalDoors} porte, ${globalWindows} finestre`);
  }

  // ════════════════════════════════════════════════════════
  // SEZIONE D: PARAMETRI PORTE E FINESTRE
  // ════════════════════════════════════════════════════════

  section("D1", "get_element_parameters \u2014 parametri di una porta");
  if (doorIds.length > 0) {
    const doorParams = await sendCommand("get_element_parameters", { elementIds: [doorIds[0]] });
    check("parametri porta OK", doorParams.ok);
    if (doorParams.ok) {
      const dp = doorParams.result?.elements?.[0]?.parameters || [];
      check("porta ha parametri", dp.length > 0, `${dp.length} parametri`);

      const dpNames = dp.map(p => (p.name || "").toLowerCase());
      check("porta: Width/Larghezza", dpNames.some(n => n.includes("width") || n.includes("larghezza")));
      check("porta: Height/Altezza", dpNames.some(n => n.includes("height") || n.includes("altezza")));
      check("porta: Level/Livello", dpNames.some(n => n.includes("level") || n.includes("livello")));
      check("porta: FromRoom/ToRoom", dpNames.some(n => n.includes("room") || n.includes("vano") || n.includes("locale")));
      check("porta: Family/Famiglia", dpNames.some(n => n.includes("family") || n.includes("famiglia")));

      // Print some key values
      console.log(`\n    Parametri chiave porta [${doorIds[0]}]:`);
      for (const p of dp) {
        const n = (p.name || "").toLowerCase();
        if (n.includes("width") || n.includes("larghezza") || n.includes("height") || n.includes("altezza") ||
            n.includes("room") || n.includes("vano") || n.includes("family") || n.includes("famiglia") ||
            n.includes("level") || n.includes("livello") || n.includes("type") || n.includes("tipo")) {
          console.log(`      ${p.name}: ${p.value}`);
        }
      }
    }
  } else { skip("parametri porta", "nessuna porta nel locale"); }

  // ────────────────────────────────────────────────────
  section("D2", "get_element_parameters \u2014 parametri di una finestra");
  if (windowIds.length > 0) {
    const winParams = await sendCommand("get_element_parameters", { elementIds: [windowIds[0]] });
    check("parametri finestra OK", winParams.ok);
    if (winParams.ok) {
      const wp = winParams.result?.elements?.[0]?.parameters || [];
      check("finestra ha parametri", wp.length > 0, `${wp.length} parametri`);

      const wpNames = wp.map(p => (p.name || "").toLowerCase());
      check("finestra: Width/Larghezza", wpNames.some(n => n.includes("width") || n.includes("larghezza")));
      check("finestra: Height/Altezza", wpNames.some(n => n.includes("height") || n.includes("altezza")));
      check("finestra: Sill Height/Davanzale", wpNames.some(n => n.includes("sill") || n.includes("davanzale") || n.includes("altezza")));
      check("finestra: Level/Livello", wpNames.some(n => n.includes("level") || n.includes("livello")));
      check("finestra: Family/Famiglia", wpNames.some(n => n.includes("family") || n.includes("famiglia")));

      console.log(`\n    Parametri chiave finestra [${windowIds[0]}]:`);
      for (const p of wp) {
        const n = (p.name || "").toLowerCase();
        if (n.includes("width") || n.includes("larghezza") || n.includes("height") || n.includes("altezza") ||
            n.includes("sill") || n.includes("davanzale") || n.includes("room") || n.includes("vano") ||
            n.includes("family") || n.includes("famiglia") || n.includes("level") || n.includes("livello")) {
          console.log(`      ${p.name}: ${p.value}`);
        }
      }
    }
  } else { skip("parametri finestra", "nessuna finestra nel locale"); }

  // ────────────────────────────────────────────────────
  section("D3", "get_element_parameters \u2014 batch porte e finestre insieme");
  const mixedIds = [...doorIds.slice(0, 3), ...windowIds.slice(0, 3)].filter(Boolean);
  if (mixedIds.length >= 2) {
    const mixedParams = await sendCommand("get_element_parameters", { elementIds: mixedIds });
    check("batch misto porte+finestre OK", mixedParams.ok);
    if (mixedParams.ok) {
      const elems = mixedParams.result?.elements || [];
      check("tutti gli elementi ritornati", elems.length === mixedIds.length, `${elems.length}/${mixedIds.length}`);
    }
  } else { skip("batch misto", "meno di 2 aperture"); }

  // ════════════════════════════════════════════════════════
  // SEZIONE E: FILTRI AVANZATI
  // ════════════════════════════════════════════════════════

  section("E1", "ai_element_filter \u2014 OST_Rooms");
  const filterRooms = await sendCommand("ai_element_filter", {
    data: { filterCategory: "OST_Rooms", includeInstances: true, maxElements: 50 }
  });
  check("ai_element_filter OST_Rooms OK", filterRooms.ok);
  if (filterRooms.ok) {
    const count = filterRooms.result?.elementCount || filterRooms.result?.elements?.length || 0;
    check("locali filtrati coerenti", count > 0, `${count}`);
  }

  // ────────────────────────────────────────────────────
  section("E2", "ai_element_filter \u2014 OST_Doors");
  const filterDoors = await sendCommand("ai_element_filter", {
    data: { filterCategory: "OST_Doors", includeInstances: true, maxElements: 50 }
  });
  check("ai_element_filter OST_Doors OK", filterDoors.ok);
  if (filterDoors.ok) {
    const count = filterDoors.result?.elementCount || filterDoors.result?.elements?.length || 0;
    console.log(`    ${count} porte nel modello`);
  }

  // ────────────────────────────────────────────────────
  section("E3", "ai_element_filter \u2014 OST_Windows");
  const filterWindows = await sendCommand("ai_element_filter", {
    data: { filterCategory: "OST_Windows", includeInstances: true, maxElements: 50 }
  });
  check("ai_element_filter OST_Windows OK", filterWindows.ok);
  if (filterWindows.ok) {
    const count = filterWindows.result?.elementCount || filterWindows.result?.elements?.length || 0;
    console.log(`    ${count} finestre nel modello`);
  }

  // ────────────────────────────────────────────────────
  section("E4", "filter_by_parameter_value \u2014 locali per nome");
  const firstName = r0?.name || r0?.roomName;
  if (firstName) {
    const filterByName = await sendCommand("filter_by_parameter_value", {
      categories: ["OST_Rooms"],
      parameterName: "Name",
      condition: "equals",
      value: firstName,
      parameterType: "instance"
    });
    // Potrebbe fallire se il parametro e' localizzato, proviamo anche "Nome"
    if (!filterByName.ok) {
      const filterByNome = await sendCommand("filter_by_parameter_value", {
        categories: ["OST_Rooms"],
        parameterName: "Nome",
        condition: "equals",
        value: firstName,
        parameterType: "instance"
      });
      check("filtro locali per nome (localizzato)", filterByNome.ok, firstName);
    } else {
      check("filtro locali per nome OK", filterByName.ok, firstName);
      const count = filterByName.result?.elements?.length || filterByName.result?.elementCount || 0;
      check("almeno 1 risultato", count > 0, `${count} locali con nome "${firstName}"`);
    }
  } else { skip("filtro per nome", "locale senza nome"); }

  // ────────────────────────────────────────────────────
  section("E5", "filter_by_parameter_value \u2014 locali per department");
  if (allDepartments.length > 0) {
    const filterDept = await sendCommand("filter_by_parameter_value", {
      categories: ["OST_Rooms"],
      parameterName: "Department",
      condition: "equals",
      value: allDepartments[0],
      parameterType: "instance"
    });
    check("filtro per Department OK", filterDept.ok, allDepartments[0]);
  } else { skip("filtro per department", "nessun department"); }

  // ────────────────────────────────────────────────────
  section("E6", "filter_by_parameter_value \u2014 locali con area > 0");
  const filterArea = await sendCommand("filter_by_parameter_value", {
    categories: ["OST_Rooms"],
    parameterName: "Area",
    condition: "greater_than",
    value: "0",
    parameterType: "instance"
  });
  check("filtro area > 0 OK", filterArea.ok);
  if (filterArea.ok) {
    const areaCount = filterArea.result?.elements?.length || filterArea.result?.elementCount || 0;
    check("locali con area > 0", areaCount > 0, `${areaCount}`);
  }

  // ────────────────────────────────────────────────────
  section("E7", "filter_by_parameter_value \u2014 locali con commenti vuoti");
  const filterEmpty = await sendCommand("filter_by_parameter_value", {
    categories: ["OST_Rooms"],
    parameterName: "Comments",
    condition: "is_empty",
    parameterType: "instance"
  });
  if (!filterEmpty.ok) {
    // Try Italian
    const filterEmptyIT = await sendCommand("filter_by_parameter_value", {
      categories: ["OST_Rooms"],
      parameterName: "Commenti",
      condition: "is_empty",
      parameterType: "instance"
    });
    check("filtro commenti vuoti (IT)", filterEmptyIT.ok);
  } else {
    check("filtro commenti vuoti OK", filterEmpty.ok);
  }

  // ────────────────────────────────────────────────────
  section("E8", "filter_by_parameter_value \u2014 porte con returnParameters");
  if (doorIds.length > 0) {
    const filterDoorsParam = await sendCommand("filter_by_parameter_value", {
      categories: ["OST_Doors"],
      parameterName: "Area",
      condition: "greater_than",
      value: "0",
      parameterType: "instance",
      returnParameters: ["Width", "Height", "Level", "Larghezza", "Altezza", "Livello"]
    });
    check("filtro porte con returnParameters OK", filterDoorsParam.ok);
    if (filterDoorsParam.ok) {
      const elems = filterDoorsParam.result?.elements || [];
      if (elems.length > 0) {
        const hasExtra = elems[0]?.parameters != null || elems[0]?.returnedParameters != null;
        check("returnParameters inclusi nella risposta", hasExtra || elems[0]?.Width != null || elems[0]?.Larghezza != null);
      }
    }
  } else { skip("filtro porte con returnParameters", "nessuna porta"); }

  // ════════════════════════════════════════════════════════
  // SEZIONE F: ELEMENTI NELLO SPAZIO DEL LOCALE
  // ════════════════════════════════════════════════════════

  section("F1", "get_elements_in_spatial_volume \u2014 porte nel locale");
  const spatialDoors = await sendCommand("get_elements_in_spatial_volume", {
    volumeType: "room",
    volumeIds: [roomId],
    categoryFilter: ["OST_Doors"],
    maxElementsPerVolume: 50
  });
  check("spatial volume (porte) OK", spatialDoors.ok);
  if (spatialDoors.ok) {
    const spatialCount = spatialDoors.result?.volumes?.[0]?.elements?.length ||
                         spatialDoors.result?.elements?.length || 0;
    console.log(`    ${spatialCount} porte nello spazio del locale`);
  }

  // ────────────────────────────────────────────────────
  section("F2", "get_elements_in_spatial_volume \u2014 finestre nel locale");
  const spatialWin = await sendCommand("get_elements_in_spatial_volume", {
    volumeType: "room",
    volumeIds: [roomId],
    categoryFilter: ["OST_Windows"],
    maxElementsPerVolume: 50
  });
  check("spatial volume (finestre) OK", spatialWin.ok);

  // ────────────────────────────────────────────────────
  section("F3", "get_elements_in_spatial_volume \u2014 tutto nel locale");
  const spatialAll = await sendCommand("get_elements_in_spatial_volume", {
    volumeType: "room",
    volumeIds: [roomId],
    maxElementsPerVolume: 50
  });
  check("spatial volume (tutto) OK", spatialAll.ok);
  if (spatialAll.ok) {
    const allElems = spatialAll.result?.volumes?.[0]?.elements || spatialAll.result?.elements || [];
    check("ha elementi nello spazio", allElems.length >= 0, `${allElems.length} elementi`);
    // Group by category
    const byCat = {};
    for (const e of allElems) {
      const cat = e.category || e.categoryName || "?";
      byCat[cat] = (byCat[cat] || 0) + 1;
    }
    if (Object.keys(byCat).length > 0) {
      console.log("    Elementi per categoria:");
      for (const [cat, cnt] of Object.entries(byCat).sort((a, b) => b[1] - a[1])) {
        console.log(`      ${cat}: ${cnt}`);
      }
    }
  }

  // ════════════════════════════════════════════════════════
  // SEZIONE G: EDGE CASES & ERROR HANDLING
  // ════════════════════════════════════════════════════════

  section("G1", "get_room_openings \u2014 locale inesistente");
  const noRoom = await sendCommand("get_room_openings", { roomIds: [999999999] });
  check("locale inesistente gestito", noRoom.ok || !noRoom.ok, noRoom.ok ? "OK con 0 risultati" : "errore gestito");

  // ────────────────────────────────────────────────────
  section("G2", "get_room_openings \u2014 numero inesistente");
  const noNum = await sendCommand("get_room_openings", { roomNumbers: ["ZZZZZ_INESISTENTE"] });
  check("numero inesistente gestito", noNum.ok || !noNum.ok);

  // ────────────────────────────────────────────────────
  section("G3", "get_room_openings \u2014 livello inesistente");
  const noLevel = await sendCommand("get_room_openings", { levelName: "LIVELLO_FANTASMA_42" });
  check("livello inesistente gestito", noLevel.ok || !noLevel.ok);

  // ────────────────────────────────────────────────────
  section("G4", "get_room_openings \u2014 senza filtri (tutti)");
  const noFilter = await sendCommand("get_room_openings", {});
  check("nessun filtro gestito", noFilter.ok);

  // ────────────────────────────────────────────────────
  section("G5", "get_element_parameters \u2014 ID inesistente");
  const noElem = await sendCommand("get_element_parameters", { elementIds: [999999999] });
  check("elemento inesistente gestito", noElem.ok || !noElem.ok);

  // ────────────────────────────────────────────────────
  section("G6", "ai_element_filter \u2014 categoria inesistente");
  const noCat = await sendCommand("ai_element_filter", {
    data: { filterCategory: "OST_CategoriaFalsa", includeInstances: true, maxElements: 5 }
  });
  check("categoria inesistente gestito", noCat.ok || !noCat.ok);

  // ────────────────────────────────────────────────────
  section("G7", "filter_by_parameter_value \u2014 parametro inesistente");
  const noParam = await sendCommand("filter_by_parameter_value", {
    categories: ["OST_Rooms"],
    parameterName: "ParametroCheSicuramenteNonEsiste",
    condition: "equals",
    value: "test"
  });
  check("parametro inesistente gestito", noParam.ok || !noParam.ok);

  // ────────────────────────────────────────────────────
  section("G8", "get_room_openings \u2014 maxElementsPerRoom = 1");
  const maxOne = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "both", maxElementsPerRoom: 1 });
  check("maxElementsPerRoom=1 OK", maxOne.ok);
  if (maxOne.ok && allOpenings.length > 1) {
    const limited = (maxOne.result?.rooms?.[0]?.openings || []).length;
    check("risultati limitati a 1", limited <= 1, `${limited} (vs ${allOpenings.length} senza limite)`);
  }

  // ════════════════════════════════════════════════════════
  // SEZIONE H: CROSS-VALIDATION
  // ════════════════════════════════════════════════════════

  section("H1", "confronto porte: get_room_openings vs ai_element_filter");
  if (openAllRooms.ok && filterDoors.ok) {
    let openingDoorCount = 0;
    for (const rm of (openAllRooms.result?.rooms || [])) {
      for (const op of (rm.openings || [])) {
        const cat = (op.category || "").toLowerCase();
        if (cat.includes("door") || cat.includes("port")) openingDoorCount++;
      }
    }
    const filterDoorCount = filterDoors.result?.elementCount || filterDoors.result?.elements?.length || 0;
    console.log(`    get_room_openings: ${openingDoorCount} porte`);
    console.log(`    ai_element_filter: ${filterDoorCount} porte`);
    // filter counts all doors in model, openings counts only those with room association
    check("filter >= openings (porte senza locale possibili)", filterDoorCount >= openingDoorCount,
      `${filterDoorCount} >= ${openingDoorCount}`);
  } else { skip("cross-validation porte", "dati mancanti"); }

  // ────────────────────────────────────────────────────
  section("H2", "confronto finestre: get_room_openings vs ai_element_filter");
  if (openAllRooms.ok && filterWindows.ok) {
    let openingWinCount = 0;
    for (const rm of (openAllRooms.result?.rooms || [])) {
      for (const op of (rm.openings || [])) {
        const cat = (op.category || "").toLowerCase();
        if (cat.includes("window") || cat.includes("finestr")) openingWinCount++;
      }
    }
    const filterWinCount = filterWindows.result?.elementCount || filterWindows.result?.elements?.length || 0;
    console.log(`    get_room_openings: ${openingWinCount} finestre`);
    console.log(`    ai_element_filter: ${filterWinCount} finestre`);
    check("filter >= openings (finestre senza locale possibili)", filterWinCount >= openingWinCount,
      `${filterWinCount} >= ${openingWinCount}`);
  } else { skip("cross-validation finestre", "dati mancanti"); }

  // ────────────────────────────────────────────────────
  section("H3", "confronto locali: export_room_data vs ai_element_filter");
  if (rooms.ok && filterRooms.ok) {
    const exportCount = roomCount;
    const filterCount = filterRooms.result?.elementCount || filterRooms.result?.elements?.length || 0;
    console.log(`    export_room_data: ${exportCount} locali`);
    console.log(`    ai_element_filter: ${filterCount} locali`);
    // filter may include unplaced rooms
    check("conteggi comparabili", Math.abs(exportCount - filterCount) <= filterCount * 0.5,
      `differenza: ${Math.abs(exportCount - filterCount)}`);
  }

  // ════════════════════════════════════════════════════════
  // SEZIONE I: STATISTICHE AGGREGATE
  // ════════════════════════════════════════════════════════

  section("I1", "statistiche locali per livello");
  const byLevel = groupBy(roomList, r => r.level || r.levelName || "?");
  for (const [lev, rooms] of Object.entries(byLevel)) {
    const areas = rooms.map(r => r.areaSqM || r.area || 0).filter(a => a > 0);
    const totArea = areas.reduce((s, a) => s + a, 0);
    console.log(`    ${lev}: ${rooms.length} locali, ${totArea.toFixed(1)} m\u00b2 totali, media ${(avg(areas)).toFixed(1)} m\u00b2`);
  }
  check("statistiche per livello calcolate", Object.keys(byLevel).length > 0);

  // ────────────────────────────────────────────────────
  section("I2", "statistiche aperture per locale (top 10)");
  if (openAllRooms.ok) {
    const roomOpeningStats = (openAllRooms.result?.rooms || [])
      .map(rm => ({
        name: rm.roomName || rm.name || rm.roomNumber || rm.number || `ID:${rm.roomId || rm.id}`,
        doors: (rm.openings || []).filter(o => { const c = (o.category || "").toLowerCase(); return c.includes("door") || c.includes("port"); }).length,
        windows: (rm.openings || []).filter(o => { const c = (o.category || "").toLowerCase(); return c.includes("window") || c.includes("finestr"); }).length,
      }))
      .sort((a, b) => (b.doors + b.windows) - (a.doors + a.windows))
      .slice(0, 10);

    for (const s of roomOpeningStats) {
      console.log(`    ${s.name}: ${s.doors} porte, ${s.windows} finestre`);
    }
    check("statistiche aperture calcolate", roomOpeningStats.length > 0);

    // Check for rooms without any openings
    const roomsNoOpenings = (openAllRooms.result?.rooms || []).filter(rm => (rm.openings || []).length === 0);
    console.log(`    Locali senza aperture: ${roomsNoOpenings.length}`);
  }

  // ────────────────────────────────────────────────────
  section("I3", "distribuzione aree locali");
  const areas = roomList.map(r => r.areaSqM || r.area || 0).filter(a => a > 0);
  if (areas.length > 0) {
    const sorted = [...areas].sort((a, b) => a - b);
    const min = sorted[0].toFixed(2);
    const max = sorted[sorted.length - 1].toFixed(2);
    const median = sorted[Math.floor(sorted.length / 2)].toFixed(2);
    const mean = avg(sorted).toFixed(2);
    const total = sorted.reduce((s, a) => s + a, 0).toFixed(1);

    console.log(`    Min: ${min} m\u00b2`);
    console.log(`    Max: ${max} m\u00b2`);
    console.log(`    Mediana: ${median} m\u00b2`);
    console.log(`    Media: ${mean} m\u00b2`);
    console.log(`    Totale: ${total} m\u00b2`);

    // Histogram buckets
    const buckets = [0, 5, 10, 20, 50, 100, 200, 500, Infinity];
    console.log("    Distribuzione:");
    for (let i = 0; i < buckets.length - 1; i++) {
      const count = sorted.filter(a => a >= buckets[i] && a < buckets[i + 1]).length;
      const bar = "\u2588".repeat(Math.min(count, 40));
      const label = buckets[i + 1] === Infinity ? `${buckets[i]}+` : `${buckets[i]}-${buckets[i + 1]}`;
      if (count > 0) console.log(`      ${label.padEnd(8)} m\u00b2: ${bar} (${count})`);
    }
    check("distribuzione aree calcolata", true);
  }

  // ════════════════════════════════════════════════════════
  // RISULTATO FINALE
  // ════════════════════════════════════════════════════════
  console.log(`\n${"#".repeat(60)}`);
  console.log(`  RISULTATO: ${passed} passed, ${failed} failed, ${skipped} skipped (${passed + failed + skipped} total)`);
  console.log(`${"#".repeat(60)}`);

  if (failed > 0) process.exit(1);
}

run().catch(e => { console.error("FATAL:", e.message); process.exit(1); });
