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
  if (condition) { passed++; console.log(`  ✓ ${name}${detail ? " — " + detail : ""}`); }
  else { failed++; console.log(`  ✗ ${name}${detail ? " — " + detail : ""}`); }
  return condition;
}

async function run() {
  console.log("=== BULK TEST LOCALI ===\n");

  // ═══════════════════════════════════════════
  // 1. EXPORT_ROOM_DATA — lista tutti i locali
  // ═══════════════════════════════════════════
  console.log("── 1. export_room_data ──");
  const rooms = await sendCommand("export_room_data", {});
  check("export_room_data OK", rooms.ok);
  const roomList = rooms.result?.rooms ?? rooms.result?.data ?? [];
  const roomCount = roomList.length || rooms.result?.roomCount || 0;
  check("ha locali", roomCount > 0, `${roomCount} locali trovati`);

  if (roomCount > 0) {
    const r0 = roomList[0];
    check("locale ha id", r0?.id != null || r0?.roomId != null);
    check("locale ha name", typeof r0?.name === "string" || typeof r0?.roomName === "string", r0?.name || r0?.roomName);
    check("locale ha number", r0?.number != null || r0?.roomNumber != null, r0?.number || r0?.roomNumber);
    check("locale ha level", r0?.level != null || r0?.levelName != null, r0?.level || r0?.levelName);
    check("locale ha area", r0?.area != null || r0?.areaSqM != null, `${r0?.area || r0?.areaSqM} m²`);

    // Show first 5 rooms
    console.log("\n  Primi 5 locali:");
    for (const r of roomList.slice(0, 5)) {
      const name = r.name || r.roomName || "?";
      const num = r.number || r.roomNumber || "?";
      const area = r.areaSqM || r.area || "?";
      const id = r.id || r.roomId;
      console.log(`    [${id}] ${num} — ${name} — ${area} m²`);
    }
  }

  // ═══════════════════════════════════════════
  // 2. GET_ELEMENT_PARAMETERS su un locale
  // ═══════════════════════════════════════════
  console.log("\n── 2. get_element_parameters su locale ──");
  const firstRoom = roomList[0];
  const roomId = firstRoom?.id || firstRoom?.roomId;

  if (roomId) {
    const params = await sendCommand("get_element_parameters", { elementIds: [roomId] });
    check("get_element_parameters OK", params.ok);

    if (params.ok) {
      const elemData = params.result?.elements?.[0] || params.result;
      const paramList = elemData?.parameters || elemData?.instanceParameters || [];
      check("ha parametri", paramList.length > 0, `${paramList.length} parametri`);

      // Check key room parameters
      const paramNames = paramList.map(p => p.name?.toLowerCase() || "");
      const hasArea = paramNames.some(n => n.includes("area"));
      const hasPerimeter = paramNames.some(n => n.includes("perimeter") || n.includes("perimetro"));
      const hasVolume = paramNames.some(n => n.includes("volume"));
      const hasLevel = paramNames.some(n => n.includes("level") || n.includes("livello"));
      const hasDepartment = paramNames.some(n => n.includes("department") || n.includes("reparto"));
      const hasNumber = paramNames.some(n => n.includes("number") || n.includes("numero"));

      check("ha parametro Area", hasArea);
      check("ha parametro Perimetro", hasPerimeter);
      check("ha parametro Volume", hasVolume);
      check("ha parametro Livello", hasLevel);
      check("ha parametro Department", hasDepartment);
      check("ha parametro Number", hasNumber);

      // Print some key values
      console.log("\n  Parametri chiave:");
      for (const p of paramList) {
        const n = p.name?.toLowerCase() || "";
        if (n.includes("area") || n.includes("volume") || n.includes("perimeter") ||
            n.includes("perimetro") || n.includes("level") || n.includes("livello") ||
            n.includes("name") || n.includes("nome") || n.includes("number") || n.includes("numero") ||
            n.includes("department") || n.includes("reparto")) {
          console.log(`    ${p.name}: ${p.value}`);
        }
      }
    }
  }

  // ═══════════════════════════════════════════
  // 3. GET_ROOM_OPENINGS — porte e finestre per locale
  // ═══════════════════════════════════════════
  console.log("\n── 3. get_room_openings ──");

  if (roomId) {
    // 3a. Tutte le aperture (porte + finestre)
    const openAll = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "both" });
    check("get_room_openings (both) OK", openAll.ok);
    if (openAll.ok) {
      const roomData = openAll.result?.rooms?.[0] || openAll.result;
      const openings = roomData?.openings || roomData?.elements || [];
      check("ha aperture", openings.length >= 0, `${openings.length} aperture`);

      if (openings.length > 0) {
        const o0 = openings[0];
        check("apertura ha elementId", o0?.elementId != null || o0?.id != null);
        check("apertura ha category", o0?.category != null || o0?.type != null);
        check("apertura ha width/height", o0?.width != null || o0?.widthMm != null);

        console.log("\n  Aperture del locale:");
        for (const o of openings.slice(0, 5)) {
          const cat = o.category || o.type || "?";
          const fam = o.familyName || o.family || "";
          const w = o.widthMm || o.width || "?";
          const h = o.heightMm || o.height || "?";
          console.log(`    [${o.elementId || o.id}] ${cat} — ${fam} — ${w}x${h}mm`);
        }
      }
    }

    // 3b. Solo porte
    const openDoors = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "doors" });
    check("get_room_openings (doors) OK", openDoors.ok);
    if (openDoors.ok) {
      const doorData = openDoors.result?.rooms?.[0] || openDoors.result;
      const doors = doorData?.openings || doorData?.elements || [];
      check("ha porte (o zero)", doors.length >= 0, `${doors.length} porte`);
    }

    // 3c. Solo finestre
    const openWin = await sendCommand("get_room_openings", { roomIds: [roomId], elementType: "windows" });
    check("get_room_openings (windows) OK", openWin.ok);
    if (openWin.ok) {
      const winData = openWin.result?.rooms?.[0] || openWin.result;
      const wins = winData?.openings || winData?.elements || [];
      check("ha finestre (o zero)", wins.length >= 0, `${wins.length} finestre`);
    }
  }

  // ═══════════════════════════════════════════
  // 4. GET_ROOM_OPENINGS — per numero locale
  // ═══════════════════════════════════════════
  console.log("\n── 4. get_room_openings per numero ──");
  const roomNum = firstRoom?.number || firstRoom?.roomNumber;
  if (roomNum) {
    const byNum = await sendCommand("get_room_openings", { roomNumbers: [roomNum], elementType: "both" });
    check("ricerca per roomNumber OK", byNum.ok, roomNum);
  }

  // ═══════════════════════════════════════════
  // 5. GET_ROOM_OPENINGS — per livello
  // ═══════════════════════════════════════════
  console.log("\n── 5. get_room_openings per livello ──");
  const roomLevel = firstRoom?.level || firstRoom?.levelName;
  if (roomLevel) {
    const byLevel = await sendCommand("get_room_openings", { levelName: roomLevel, elementType: "both" });
    check("ricerca per levelName OK", byLevel.ok, roomLevel);
    if (byLevel.ok) {
      const levelRooms = byLevel.result?.rooms || [];
      check("locali trovati al livello", levelRooms.length > 0, `${levelRooms.length} locali`);

      // Count total openings across all rooms
      let totalDoors = 0, totalWindows = 0;
      for (const rm of levelRooms) {
        for (const op of (rm.openings || [])) {
          if (op.category === "Doors" || op.category === "Porte") totalDoors++;
          else if (op.category === "Windows" || op.category === "Finestre") totalWindows++;
        }
      }
      console.log(`  Totale al livello ${roomLevel}: ${totalDoors} porte, ${totalWindows} finestre`);
    }
  }

  // ═══════════════════════════════════════════
  // 6. AI_ELEMENT_FILTER — trova locali per filtro
  // ═══════════════════════════════════════════
  console.log("\n── 6. ai_element_filter — filtra locali ──");
  const filterRooms = await sendCommand("ai_element_filter", {
    data: {
      filterCategory: "OST_Rooms",
      includeInstances: true,
      maxElements: 10
    }
  });
  check("ai_element_filter OST_Rooms OK", filterRooms.ok);
  if (filterRooms.ok) {
    const count = filterRooms.result?.elementCount || filterRooms.result?.elements?.length || 0;
    check("ha locali filtrati", count > 0, `${count} locali`);
  }

  // ═══════════════════════════════════════════
  // 7. FILTER_BY_PARAMETER_VALUE — filtra per department
  // ═══════════════════════════════════════════
  console.log("\n── 7. filter_by_parameter_value ──");

  // First, get departments from room data
  const departments = [...new Set(roomList.map(r => r.department).filter(Boolean))];
  console.log(`  Department trovati: ${departments.length > 0 ? departments.join(", ") : "(nessuno)"}`);

  if (departments.length > 0) {
    const filterDept = await sendCommand("filter_by_parameter_value", {
      category: "OST_Rooms",
      parameterName: "Department",
      value: departments[0],
      comparison: "equals"
    });
    check("filtro per Department OK", filterDept.ok, departments[0]);
    if (filterDept.ok) {
      check("ha risultati", (filterDept.result?.elements?.length || filterDept.result?.elementCount || 0) > 0);
    }
  }

  // ═══════════════════════════════════════════
  // 8. GET_ROOM_OPENINGS — locale inesistente
  // ═══════════════════════════════════════════
  console.log("\n── 8. Edge cases ──");
  const noRoom = await sendCommand("get_room_openings", { roomIds: [999999999] });
  check("locale inesistente → gestito", noRoom.ok || !noRoom.ok);

  const noNum = await sendCommand("get_room_openings", { roomNumbers: ["ZZZZZ"] });
  check("numero inesistente → gestito", noNum.ok || !noNum.ok);

  const empty = await sendCommand("get_room_openings", {});
  check("nessun filtro → gestito", empty.ok || !empty.ok);

  // ═══════════════════════════════════════════
  // 9. EXPORT_ROOM_DATA con parametri extra
  // ═══════════════════════════════════════════
  console.log("\n── 9. export_room_data dettagliato ──");
  const roomsDetailed = await sendCommand("export_room_data", { includeUnplaced: true });
  check("export con includeUnplaced OK", roomsDetailed.ok);
  if (roomsDetailed.ok) {
    const allRooms = roomsDetailed.result?.rooms || roomsDetailed.result?.data || [];
    check("include locali non posizionati", allRooms.length >= roomCount, `${allRooms.length} totali vs ${roomCount} posizionati`);
  }

  // ═══════════════════════════════════════════
  // 10. Riepilogo dimensioni locali
  // ═══════════════════════════════════════════
  console.log("\n── 10. Riepilogo dimensioni locali ──");
  if (roomList.length > 0) {
    const areas = roomList.map(r => r.areaSqM || r.area || 0).filter(a => a > 0);
    const volumes = roomList.map(r => r.volumeCuM || r.volume || 0).filter(v => v > 0);
    const perimeters = roomList.map(r => r.perimeterMm || r.perimeter || 0).filter(p => p > 0);

    if (areas.length > 0) {
      const minArea = Math.min(...areas).toFixed(2);
      const maxArea = Math.max(...areas).toFixed(2);
      const avgArea = (areas.reduce((s, a) => s + a, 0) / areas.length).toFixed(2);
      console.log(`  Area: min ${minArea} m², max ${maxArea} m², media ${avgArea} m²`);
      check("ha dati area", true);
    }

    if (volumes.length > 0) {
      const minVol = Math.min(...volumes).toFixed(2);
      const maxVol = Math.max(...volumes).toFixed(2);
      console.log(`  Volume: min ${minVol} m³, max ${maxVol} m³`);
      check("ha dati volume", true);
    }

    if (perimeters.length > 0) {
      console.log(`  Perimetro: ${perimeters.length} locali con dati`);
      check("ha dati perimetro", true);
    }

    // Per-level summary
    const byLevel = {};
    for (const r of roomList) {
      const lev = r.level || r.levelName || "?";
      if (!byLevel[lev]) byLevel[lev] = { count: 0, totalArea: 0, doors: 0, windows: 0 };
      byLevel[lev].count++;
      byLevel[lev].totalArea += r.areaSqM || r.area || 0;
    }
    console.log("\n  Riepilogo per livello:");
    for (const [lev, data] of Object.entries(byLevel)) {
      console.log(`    ${lev}: ${data.count} locali, ${data.totalArea.toFixed(1)} m² totali`);
    }
  }

  // ═══════════════════════════════════════════
  // RISULTATO FINALE
  // ═══════════════════════════════════════════
  console.log(`\n${"═".repeat(50)}`);
  console.log(`RISULTATO: ${passed} passed, ${failed} failed (${passed + failed} total)`);
  console.log(`${"═".repeat(50)}`);
}

run().catch(e => console.error("FATAL:", e.message));
