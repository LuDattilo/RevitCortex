/**
 * Cerca tutti gli elementi creati dal bulk test (nome contiene "TEST")
 * e li evidenzia in Revit con colore rosso.
 */
import { Socket } from "net";

const HOST = "localhost";
const PORT = 8080;
let requestId = 0;

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

async function run() {
  console.log("=== CERCA E EVIDENZIA ELEMENTI TEST/MCP ===\n");

  const allFoundIds = new Set();
  const findings = [];

  // 1. Cerca views con "TEST" nel nome
  console.log("-- Cerco viste con 'TEST' nel nome...");
  const views = await sendCommand("filter_by_parameter_value", {
    parameterName: "View Name",
    condition: "contains",
    value: "TEST",
    parameterType: "instance",
    returnParameters: ["View Name"]
  });
  if (views.ok) {
    const elems = views.result?.elements || [];
    for (const e of elems) {
      allFoundIds.add(e.elementId || e.id);
      findings.push({ id: e.elementId || e.id, type: "View", name: e["View Name"] || e.name || "?" });
    }
    console.log(`  ${elems.length} viste trovate`);
  }

  // 2. Cerca sheets con "TEST" o "T-" nel numero
  console.log("-- Cerco tavole con 'TEST', 'T-', 'MCP' ...");
  for (const searchVal of ["TEST", "T-", "WT-", "OT-", "MCP"]) {
    const sheets = await sendCommand("filter_by_parameter_value", {
      parameterName: "Sheet Number",
      condition: "contains",
      value: searchVal,
      parameterType: "instance",
      returnParameters: ["Sheet Number", "Sheet Name"]
    });
    if (sheets.ok) {
      const elems = sheets.result?.elements || [];
      for (const e of elems) {
        const eid = e.elementId || e.id;
        if (!allFoundIds.has(eid)) {
          allFoundIds.add(eid);
          findings.push({ id: eid, type: "Sheet", name: `${e["Sheet Number"] || ""} - ${e["Sheet Name"] || ""}` });
        }
      }
    }
  }

  // 3. Cerca locali con "TEST" nel nome
  console.log("-- Cerco locali con 'TEST' nel nome...");
  const rooms = await sendCommand("filter_by_parameter_value", {
    categories: ["OST_Rooms"],
    parameterName: "Name",
    condition: "contains",
    value: "TEST",
    parameterType: "instance",
    returnParameters: ["Name", "Number"]
  });
  if (rooms.ok) {
    const elems = rooms.result?.elements || [];
    for (const e of elems) {
      const eid = e.elementId || e.id;
      if (!allFoundIds.has(eid)) {
        allFoundIds.add(eid);
        findings.push({ id: eid, type: "Room", name: e.Name || e.name || "?" });
      }
    }
    console.log(`  ${elems.length} locali trovati`);
  }

  // 4. Cerca schedules con "TEST" nel nome
  console.log("-- Cerco schedules con 'TEST' nel nome...");
  const schData = await sendCommand("get_schedule_data", {});
  if (schData.ok) {
    const schedules = schData.result?.schedules || schData.result || [];
    for (const s of schedules) {
      const name = s.name || s.scheduleName || "";
      if (name.toUpperCase().includes("TEST")) {
        const eid = s.id || s.scheduleId;
        if (eid && !allFoundIds.has(eid)) {
          allFoundIds.add(eid);
          findings.push({ id: eid, type: "Schedule", name });
        }
      }
    }
  }

  // 5. Cerca materiali con "TEST" nel nome
  console.log("-- Cerco materiali con 'TEST' nel nome...");
  const mats = await sendCommand("get_materials", {});
  if (mats.ok) {
    const matList = mats.result?.materials || mats.result || [];
    for (const m of matList) {
      const name = m.name || m.materialName || "";
      if (name.toUpperCase().includes("TEST") || name.toUpperCase().includes("MCP")) {
        const eid = m.id || m.materialId;
        if (eid && !allFoundIds.has(eid)) {
          allFoundIds.add(eid);
          findings.push({ id: eid, type: "Material", name });
        }
      }
    }
  }

  // 6. Cerca livelli con "TEST" nel nome
  console.log("-- Cerco livelli con 'TEST' nel nome...");
  const lvls = await sendCommand("filter_by_parameter_value", {
    parameterName: "Name",
    condition: "contains",
    value: "TEST",
    parameterType: "instance",
    returnParameters: ["Name"]
  });
  if (lvls.ok) {
    const elems = lvls.result?.elements || [];
    for (const e of elems) {
      const eid = e.elementId || e.id;
      if (!allFoundIds.has(eid)) {
        allFoundIds.add(eid);
        findings.push({ id: eid, type: "Level/Grid/Other", name: e.Name || e.name || "?" });
      }
    }
  }

  // 7. Cerca con "MCP" nel nome/commenti
  console.log("-- Cerco elementi con 'MCP' nei commenti...");
  const mcp = await sendCommand("filter_by_parameter_value", {
    parameterName: "Comments",
    condition: "contains",
    value: "MCP",
    parameterType: "instance",
    returnParameters: ["Comments"]
  });
  if (mcp.ok) {
    const elems = mcp.result?.elements || [];
    for (const e of elems) {
      const eid = e.elementId || e.id;
      if (!allFoundIds.has(eid)) {
        allFoundIds.add(eid);
        findings.push({ id: eid, type: "Element (MCP comment)", name: e.Comments || "?" });
      }
    }
    console.log(`  ${elems.length} elementi con 'MCP' nei commenti`);
  }

  // 8. Cerca con "BulkTest" nei commenti
  console.log("-- Cerco elementi con 'BulkTest' nei commenti...");
  const bt = await sendCommand("filter_by_parameter_value", {
    parameterName: "Comments",
    condition: "contains",
    value: "BulkTest",
    parameterType: "instance",
    returnParameters: ["Comments"]
  });
  if (bt.ok) {
    const elems = bt.result?.elements || [];
    for (const e of elems) {
      const eid = e.elementId || e.id;
      if (!allFoundIds.has(eid)) {
        allFoundIds.add(eid);
        findings.push({ id: eid, type: "Element (BulkTest comment)", name: e.Comments || "?" });
      }
    }
    console.log(`  ${elems.length} elementi con 'BulkTest' nei commenti`);
  }

  // === REPORT ===
  console.log(`\n${"=".repeat(60)}`);
  console.log(`  TROVATI: ${findings.length} elementi creati dal test`);
  console.log(`${"=".repeat(60)}`);
  for (const f of findings) {
    console.log(`  [${f.id}] ${f.type.padEnd(25)} ${f.name}`);
  }

  // === EVIDENZIA IN REVIT ===
  const modelIds = findings
    .filter(f => !["View", "Sheet", "Schedule", "Material"].includes(f.type))
    .map(f => f.id)
    .filter(Boolean);

  const allIds = findings.map(f => f.id).filter(Boolean);

  if (allIds.length > 0) {
    console.log(`\n-- Seleziono ${allIds.length} elementi in Revit...`);
    const sel = await sendCommand("operate_element", {
      data: { elementIds: allIds, action: "select" }
    });
    console.log(sel.ok ? "  ✓ Elementi selezionati" : "  ✗ Selezione fallita");

    // Colora gli elementi del modello in rosso
    if (modelIds.length > 0) {
      console.log(`-- Coloro ${modelIds.length} elementi modello in rosso...`);
      const color = await sendCommand("operate_element", {
        data: { elementIds: modelIds, action: "setcolor", colorValue: [255, 50, 50] }
      });
      console.log(color.ok ? "  ✓ Colorati in rosso" : `  ✗ Colorazione fallita`);
    }
  } else {
    console.log("\n  Nessun elemento trovato da evidenziare.");
  }

  console.log("\n=== FINE ===");
}

run().catch(e => console.error("FATAL:", e.message));
