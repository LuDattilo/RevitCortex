import type { ParsedJournal, JournalLine } from "./parser.js";

// ── Helpers ─────────────────────────────────────────────────────────────────

function getTimestamps(lines: JournalLine[]): Date[] {
  return lines
    .filter((l) => l.type === "timestamp" && l.data.datetime)
    .map((l) => l.data.datetime as Date);
}

function getLastTimestamp(lines: JournalLine[]): Date | null {
  const ts = getTimestamps(lines);
  return ts.length > 0 ? ts[ts.length - 1] : null;
}

function minutesBetween(a: Date | null, b: Date | null): number | null {
  if (!a || !b) return null;
  return Math.round((b.getTime() - a.getTime()) / 60_000);
}

function isoString(d: Date | null): string | null {
  return d ? d.toISOString() : null;
}

// ── Summary ─────────────────────────────────────────────────────────────────

function analyzeSummary(journal: ParsedJournal) {
  const { header, lines } = journal;
  const endTime = getLastTimestamp(lines);
  const duration = minutesBetween(header.startTime, endTime);

  // Documents opened
  const docs = lines
    .filter((l) => l.type === "basic_file_info")
    .map((l) => ({
      filename: l.data.Filename || l.data.filename || "",
      path: l.data.CentralPath || l.data.centralpath || l.data.Path || "",
      worksharing: l.data.Worksharing || l.data.worksharing || "",
      user: l.data.Username || l.data.username || "",
    }));

  // Top commands
  const cmdCounts = new Map<string, number>();
  for (const l of lines) {
    if (l.type === "command") {
      const name = (l.data.description as string) || "Unknown";
      cmdCounts.set(name, (cmdCounts.get(name) || 0) + 1);
    }
  }
  const commandsTop10 = [...cmdCounts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 10)
    .map(([name, count]) => ({ name, count }));

  // Errors
  const apiErrors = lines.filter(
    (l) => l.type === "api_message" && l.data.level === "ERROR"
  ).length;
  const txFailures = lines.filter(
    (l) =>
      l.type === "data" &&
      typeof l.data.key === "string" &&
      l.data.key.toLowerCase().includes("transaction") &&
      typeof l.data.value === "string" &&
      (l.data.value.toLowerCase().includes("fail") ||
        l.data.value.toLowerCase().includes("roll"))
  ).length;

  // Crash detection
  const hasQuit = lines.some(
    (l) =>
      l.type === "command" &&
      typeof l.data.commandId === "string" &&
      l.data.commandId.includes("EXIT")
  );

  // Memory peak
  const memLines = lines.filter((l) => l.type === "memory");
  const peakRAM = memLines.length > 0
    ? Math.max(...memLines.map((l) => (l.data.ramPeak as number) || 0))
    : null;
  const peakVM = memLines.length > 0
    ? Math.max(...memLines.map((l) => (l.data.vmPeak as number) || 0))
    : null;

  const gdiLines = lines.filter((l) => l.type === "gdi");
  const gdiPeak = gdiLines.length > 0
    ? Math.max(...gdiLines.map((l) => (l.data.gdiUsed as number) || 0))
    : null;

  // Add-ins
  const addinLines = lines.filter((l) => l.type === "addin_manifest");
  const failedAddins = addinLines.filter(
    (l) => l.data.LoadResult === "Failed" || l.data.loadresult === "Failed"
  );

  return {
    session: {
      start: isoString(header.startTime),
      end: isoString(endTime),
      durationMinutes: duration,
      revitVersion: header.revitVersion,
      build: header.build,
    },
    documents: docs,
    commandsTop10,
    errorSummary: {
      apiErrors,
      transactionFailures: txFailures,
      crashDetected: !hasQuit,
    },
    memory: { peakRAM_MB: peakRAM, peakVM_MB: peakVM, gdiPeak },
    addins: {
      loaded: addinLines.length,
      failed: failedAddins.length,
    },
    malformedLines: journal.malformedCount,
  };
}

// ── Session Diagnostics ─────────────────────────────────────────────────────

function analyzeSessionDiagnostics(journal: ParsedJournal) {
  const { header, lines } = journal;
  const endTime = getLastTimestamp(lines);

  const hasQuit = lines.some(
    (l) =>
      l.type === "command" &&
      typeof l.data.commandId === "string" &&
      l.data.commandId.includes("EXIT")
  );

  const apiErrors = lines
    .filter((l) => l.type === "api_message" && l.data.level === "ERROR")
    .map((l) => ({ lineNum: l.lineNum, message: l.data.message }));

  const txFailures = lines
    .filter(
      (l) =>
        l.type === "data" &&
        typeof l.data.key === "string" &&
        l.data.key.toLowerCase().includes("transaction") &&
        typeof l.data.value === "string" &&
        (l.data.value.toLowerCase().includes("fail") ||
          l.data.value.toLowerCase().includes("roll"))
    )
    .map((l) => ({
      lineNum: l.lineNum,
      transactionName: l.data.key,
      message: l.data.value,
    }));

  // Find the last command before end
  const commandLines = lines.filter((l) => l.type === "command");
  const lastCmd = commandLines.length > 0 ? commandLines[commandLines.length - 1] : null;

  return {
    crashDetected: !hasQuit,
    abnormalTermination: !hasQuit,
    apiErrors: apiErrors.slice(0, 50),
    apiErrorCount: apiErrors.length,
    transactionFailures: txFailures.slice(0, 50),
    transactionFailureCount: txFailures.length,
    sessionDuration: minutesBetween(header.startTime, endTime)
      ? `${minutesBetween(header.startTime, endTime)} minutes`
      : "unknown",
    lastCommand: lastCmd
      ? { lineNum: lastCmd.lineNum, name: lastCmd.data.description }
      : null,
  };
}

// ── Memory Profile ──────────────────────────────────────────────────────────

function analyzeMemoryProfile(journal: ParsedJournal) {
  const { lines, header } = journal;
  const startTime = header.startTime;

  const memLines = lines.filter((l) => l.type === "memory");
  const gdiLines = lines.filter((l) => l.type === "gdi");

  // Build checkpoints with approximate timestamps
  const checkpoints = memLines.map((ml) => {
    // Find closest preceding timestamp
    const ts = findClosestTimestamp(ml.lineNum, lines);
    return {
      timestamp: isoString(ts),
      lineNum: ml.lineNum,
      vmUsed_MB: ml.data.vmUsed as number,
      vmPeak_MB: ml.data.vmPeak as number,
      ramUsed_MB: ml.data.ramUsed as number,
      ramPeak_MB: ml.data.ramPeak as number,
    };
  });

  // Attach GDI to nearest memory checkpoint
  for (const g of gdiLines) {
    const nearest = checkpoints.reduce<{ cp: typeof checkpoints[0] | null; dist: number }>(
      (best, cp) => {
        const d = Math.abs(cp.lineNum - g.lineNum);
        return d < best.dist ? { cp, dist: d } : best;
      },
      { cp: null, dist: Infinity }
    );
    if (nearest.cp) {
      (nearest.cp as Record<string, unknown>).gdiUsed = g.data.gdiUsed;
    }
  }

  // Trend analysis
  let trend: "stable" | "increasing" | "spike" = "stable";
  if (checkpoints.length >= 3) {
    const ramValues = checkpoints.map((c) => c.ramUsed_MB);
    const firstHalf = ramValues.slice(0, Math.floor(ramValues.length / 2));
    const secondHalf = ramValues.slice(Math.floor(ramValues.length / 2));
    const avgFirst = firstHalf.reduce((a, b) => a + b, 0) / firstHalf.length;
    const avgSecond = secondHalf.reduce((a, b) => a + b, 0) / secondHalf.length;
    if (avgSecond > avgFirst * 1.3) trend = "increasing";
    const maxRam = Math.max(...ramValues);
    const avgAll = ramValues.reduce((a, b) => a + b, 0) / ramValues.length;
    if (maxRam > avgAll * 2) trend = "spike";
  }

  // Leak rate
  const endTime = getLastTimestamp(lines);
  const durationMinutes = minutesBetween(startTime, endTime);
  let leakRate: number | null = null;
  if (checkpoints.length >= 2 && durationMinutes && durationMinutes > 0) {
    const first = checkpoints[0].ramUsed_MB;
    const last = checkpoints[checkpoints.length - 1].ramUsed_MB;
    const delta = last - first;
    if (delta > 0) leakRate = Math.round((delta / (durationMinutes / 60)) * 10) / 10;
  }

  return {
    checkpointCount: checkpoints.length,
    checkpoints: checkpoints.slice(0, 100),
    trend,
    peakVM_MB: checkpoints.length > 0 ? Math.max(...checkpoints.map((c) => c.vmPeak_MB)) : null,
    peakRAM_MB: checkpoints.length > 0 ? Math.max(...checkpoints.map((c) => c.ramPeak_MB)) : null,
    gdiPeak: gdiLines.length > 0 ? Math.max(...gdiLines.map((g) => (g.data.gdiUsed as number) || 0)) : null,
    estimatedLeakRate_MB_per_hour: leakRate,
  };
}

function findClosestTimestamp(
  lineNum: number,
  lines: JournalLine[]
): Date | null {
  let closest: Date | null = null;
  for (const l of lines) {
    if (l.lineNum > lineNum) break;
    if (l.type === "timestamp" && l.data.datetime) {
      closest = l.data.datetime as Date;
    }
  }
  return closest;
}

// ── Command Usage ───────────────────────────────────────────────────────────

function analyzeCommandUsage(journal: ParsedJournal) {
  const { lines } = journal;

  const cmdMap = new Map<string, { count: number; first: Date | null; last: Date | null }>();
  const ribbonMap = new Map<string, number>();
  const hourlyBuckets = new Map<number, number>();

  let lastTimestamp: Date | null = null;

  for (const l of lines) {
    if (l.type === "timestamp" && l.data.datetime) {
      lastTimestamp = l.data.datetime as Date;
    }

    if (l.type === "command") {
      const name = (l.data.description as string) || "Unknown";
      const entry = cmdMap.get(name) || { count: 0, first: null, last: null };
      entry.count++;
      if (!entry.first && lastTimestamp) entry.first = lastTimestamp;
      if (lastTimestamp) entry.last = lastTimestamp;
      cmdMap.set(name, entry);

      if (lastTimestamp) {
        const hour = lastTimestamp.getHours();
        hourlyBuckets.set(hour, (hourlyBuckets.get(hour) || 0) + 1);
      }
    }

    if (l.type === "ribbon_event") {
      const evt = l.data.event as string;
      // Extract tab name from "TabActivated:TabName"
      const tab = evt.includes(":") ? evt.split(":")[1] : evt;
      ribbonMap.set(tab, (ribbonMap.get(tab) || 0) + 1);
    }
  }

  const commands = [...cmdMap.entries()]
    .sort((a, b) => b[1].count - a[1].count)
    .map(([name, data]) => ({
      name,
      count: data.count,
      firstUsed: isoString(data.first),
      lastUsed: isoString(data.last),
    }));

  const ribbonEvents = [...ribbonMap.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([tab, count]) => ({ tab, count }));

  const timeline = [...hourlyBuckets.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([hour, commandCount]) => ({ hour, commandCount }));

  return {
    totalCommands: commands.reduce((sum, c) => sum + c.count, 0),
    uniqueCommands: commands.length,
    commands: commands.slice(0, 100),
    ribbonEvents: ribbonEvents.slice(0, 30),
    timeline,
  };
}

// ── Add-in Audit ────────────────────────────────────────────────────────────

function analyzeAddinAudit(journal: ParsedJournal) {
  const { lines } = journal;

  const addinLines = lines.filter((l) => l.type === "addin_manifest");
  const apiErrors = lines.filter(
    (l) => l.type === "api_message" && l.data.level === "ERROR"
  );
  const externalEvents = lines.filter((l) => l.type === "external_event");

  const addins = addinLines.map((l) => {
    const name = (l.data.Name || l.data.name || "") as string;
    const vendor = (l.data.Vendor || l.data.vendor || "") as string;
    const version = (l.data.Version || l.data.version || "") as string;
    const guid = (l.data.GUID || l.data.guid || "") as string;
    const loadTime = (l.data.LoadTime || l.data.loadtime || "") as string;
    const signed = (l.data.CodeSigning || l.data.codesigning || "") as string;

    // Count API errors mentioning this add-in
    const addinErrors = apiErrors.filter(
      (e) =>
        typeof e.data.message === "string" &&
        e.data.message.toLowerCase().includes(name.toLowerCase())
    ).length;

    // Count external events for this add-in
    const addinEvents = externalEvents.filter(
      (e) =>
        typeof e.data.Handler === "string" &&
        e.data.Handler.toLowerCase().includes(name.toLowerCase())
    ).length;

    return {
      name,
      vendor,
      version,
      guid,
      loadTimeMs: loadTime ? parseFloat(loadTime) : null,
      signed: signed === "True" || signed === "true",
      apiErrors: addinErrors,
      externalEvents: addinEvents,
    };
  });

  const failedLoads = addins.filter(
    (a) =>
      addinLines.some(
        (l) =>
          (l.data.Name === a.name || l.data.name === a.name) &&
          (l.data.LoadResult === "Failed" || l.data.loadresult === "Failed")
      )
  );

  const totalLoadTime = addins.reduce(
    (sum, a) => sum + (a.loadTimeMs || 0),
    0
  );

  return {
    addinCount: addins.length,
    addins,
    totalLoadTimeMs: Math.round(totalLoadTime),
    failedLoads: failedLoads.map((a) => ({ name: a.name, vendor: a.vendor })),
    externalEventCount: externalEvents.length,
  };
}

// ── Transaction Log ─────────────────────────────────────────────────────────

function analyzeTransactionLog(journal: ParsedJournal) {
  const { lines } = journal;

  const transactions: Array<{
    lineNum: number;
    timestamp: string | null;
    name: string;
    result: "success" | "failed" | "rolled_back";
  }> = [];

  let lastTimestamp: Date | null = null;

  for (const l of lines) {
    if (l.type === "timestamp" && l.data.datetime) {
      lastTimestamp = l.data.datetime as Date;
    }
    if (l.type === "data" && typeof l.data.key === "string") {
      const key = l.data.key.toLowerCase();
      const value = ((l.data.value as string) || "").toLowerCase();

      if (key.includes("transaction")) {
        let result: "success" | "failed" | "rolled_back" = "success";
        if (value.includes("fail")) result = "failed";
        else if (value.includes("roll")) result = "rolled_back";
        else if (key.includes("successful") || value.includes("success")) result = "success";

        transactions.push({
          lineNum: l.lineNum,
          timestamp: isoString(lastTimestamp),
          name: l.data.value as string || l.data.key as string,
          result,
        });
      }
    }
  }

  const successCount = transactions.filter((t) => t.result === "success").length;
  const failureCount = transactions.filter((t) => t.result === "failed").length;
  const rollbackCount = transactions.filter((t) => t.result === "rolled_back").length;

  return {
    totalTransactions: transactions.length,
    successCount,
    failureCount,
    rollbackCount,
    transactions: transactions.slice(0, 200),
  };
}

// ── Full (all combined) ─────────────────────────────────────────────────────

function analyzeFull(journal: ParsedJournal) {
  return {
    summary: analyzeSummary(journal),
    sessionDiagnostics: analyzeSessionDiagnostics(journal),
    memoryProfile: analyzeMemoryProfile(journal),
    commandUsage: analyzeCommandUsage(journal),
    addinAudit: analyzeAddinAudit(journal),
    transactionLog: analyzeTransactionLog(journal),
  };
}

// ── Dispatcher ──────────────────────────────────────────────────────────────

export function runAnalysis(
  journal: ParsedJournal,
  analysisType: string
): unknown {
  switch (analysisType) {
    case "summary":
      return analyzeSummary(journal);
    case "session_diagnostics":
      return analyzeSessionDiagnostics(journal);
    case "memory_profile":
      return analyzeMemoryProfile(journal);
    case "command_usage":
      return analyzeCommandUsage(journal);
    case "addin_audit":
      return analyzeAddinAudit(journal);
    case "transaction_log":
      return analyzeTransactionLog(journal);
    case "full":
      return analyzeFull(journal);
    default:
      throw new Error(`Unknown analysis type: ${analysisType}`);
  }
}
