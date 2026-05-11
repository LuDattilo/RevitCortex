import * as React from "react";
import * as ReactDOM from "react-dom";
import powerbi from "powerbi-visuals-api";
import IVisual = powerbi.extensibility.visual.IVisual;
import VisualConstructorOptions = powerbi.extensibility.visual.VisualConstructorOptions;
import VisualUpdateOptions = powerbi.extensibility.visual.VisualUpdateOptions;

const BASE_URL = "http://localhost:27016";
const PBI_SELECT_URL = `${BASE_URL}/pbi-select`;
const PBI_COLOR_URL = `${BASE_URL}/pbi-color`;
const PBI_RESET_URL = `${BASE_URL}/pbi-reset-overrides`;
const PBI_CREATE_VIEW_URL = `${BASE_URL}/pbi-create-view`;
const CHECK_INTERVAL_MS = 30_000;

// ─── RevitCortex palette ────────────────────────────────────────────────────
// Aligned with IconFactory.cs in the C# plugin (single source of truth)
const PALETTE = {
  teal:        "#00838F", // primary
  tealDark:    "#006064", // hover / borders
  indigo:      "#5C6BC0", // accent
  activeGreen: "#2E7D32", // connected
  inactiveGray:"#616161", // disconnected
  amber:       "#F59E0B", // warning
  bg:          "#FFFFFF",
  bgSubtle:    "#F5F5F5",
  border:      "#E0E0E0",
  textPrimary: "#212121",
  textMuted:   "#757575",
  successBg:   "#DFF6DD",
  successText: "#107C10",
} as const;

// ─── i18n ───────────────────────────────────────────────────────────────────
type Lang = "en" | "it";

interface Strings {
  title: string;
  selectButton: string;
  isolateButton: string;
  colorButton: string;
  resetButton: string;
  createViewButton: string;
  highlighted: string;     // "evidenziati" / "highlighted"
  filtered: string;        // "filtrati" / "filtered"
  totals: string;          // "totali" / "total"
  sent: (n: number) => string;
  colored: (n: number) => string;
  reset: (n: number) => string;
  viewCreated: (name: string) => string;
  connected: string;
  notConnected: string;
  nothingToSelect: string;
  hintConnect: string;
  hintSelect: (n: number, label: string) => string;
  isolateHint: string;
  colorHint: string;
  resetHint: string;
  createViewHint: string;
  noColorColumnHint: string;
}

const STRINGS: Record<Lang, Strings> = {
  en: {
    title: "RevitCortex Selection",
    selectButton: "Select in Revit",
    isolateButton: "Isolate in Revit",
    colorButton: "Color in Revit",
    resetButton: "Reset overrides",
    createViewButton: "Create 3D view from selection",
    highlighted: "highlighted",
    filtered: "filtered",
    totals: "total",
    sent: (n) => `✓ Sent ${n} element${n === 1 ? "" : "s"}`,
    colored: (n) => `✓ Colored ${n} element${n === 1 ? "" : "s"}`,
    reset: (n) => `✓ Reset ${n} element${n === 1 ? "" : "s"}`,
    viewCreated: (name) => `✓ View created: ${name}`,
    connected: "Connected to Revit",
    notConnected: "RevitCortex not running",
    nothingToSelect: "Nothing to select",
    hintConnect: "RevitCortex not active — start the server in Revit",
    hintSelect: (n, label) => `Select ${n} ${label} element${n === 1 ? "" : "s"} in Revit`,
    isolateHint: "Isolate the elements in the active view (in addition to selection)",
    colorHint: "Apply color override on the active view using the mapped Color column",
    resetHint: "Reset all view overrides on the active view",
    createViewHint: "Create a new 3D view with a section box around the elements (added to Project Browser; current view is preserved)",
    noColorColumnHint: "Map a 'Color (hex)' column to enable this button",
  },
  it: {
    title: "RevitCortex Selection",
    selectButton: "Seleziona in Revit",
    isolateButton: "Isola in Revit",
    colorButton: "Colora in Revit",
    resetButton: "Reset override",
    createViewButton: "Crea vista 3D da selezione",
    highlighted: "evidenziati",
    filtered: "filtrati",
    totals: "totali",
    sent: (n) => `✓ Inviati ${n} element${n === 1 ? "o" : "i"}`,
    colored: (n) => `✓ Colorati ${n} element${n === 1 ? "o" : "i"}`,
    reset: (n) => `✓ Reset di ${n} element${n === 1 ? "o" : "i"}`,
    viewCreated: (name) => `✓ Vista creata: ${name}`,
    connected: "Connesso a Revit",
    notConnected: "RevitCortex non attivo",
    nothingToSelect: "Nessun elemento da selezionare",
    hintConnect: "RevitCortex non attivo — avvia il server in Revit",
    hintSelect: (n, label) => `Seleziona ${n} elementi ${label} in Revit`,
    isolateHint: "Isola gli elementi nella vista attiva (oltre alla selezione)",
    colorHint: "Applica un override colore sulla vista attiva usando la colonna 'Color' mappata",
    resetHint: "Rimuove tutti gli override grafici sulla vista attiva",
    createViewHint: "Crea una nuova vista 3D con section box attorno agli elementi (aggiunta al Project Browser; la vista corrente resta invariata)",
    noColorColumnHint: "Mappa una colonna 'Color (hex)' per abilitare questo pulsante",
  },
};

/**
 * Detect language from Power BI culture, then from the browser, then default to English.
 * PBI passes a culture string like "it-IT", "en-US" via VisualHost.locale.
 */
function detectLang(host?: powerbi.extensibility.visual.IVisualHost): Lang {
  const candidates: string[] = [];
  if (host?.locale) candidates.push(host.locale);
  if (typeof navigator !== "undefined") {
    if (navigator.language) candidates.push(navigator.language);
    if (Array.isArray(navigator.languages)) candidates.push(...navigator.languages);
  }
  for (const c of candidates) {
    if (c && c.toLowerCase().startsWith("it")) return "it";
  }
  return "en";
}

// ─── Data helpers ──────────────────────────────────────────────────────────

interface ColoredRow {
  id: number;
  hex: string;
}

interface ExtractedData {
  filteredIds: number[];
  highlightedIds: number[];
  filteredColored: ColoredRow[];
  highlightedColored: ColoredRow[];
  hasColorColumn: boolean;
}

/**
 * Extract all the data we need from the table data view in a single pass.
 * Column order matches the `dataViewMappings.table.rows.select` declaration:
 *   [0] elementIds
 *   [1] colorHex (optional)
 */
function extractData(dataView: powerbi.DataView): ExtractedData {
  const empty: ExtractedData = {
    filteredIds: [],
    highlightedIds: [],
    filteredColored: [],
    highlightedColored: [],
    hasColorColumn: false,
  };
  const rows = dataView.table?.rows ?? [];
  if (rows.length === 0) return empty;

  // Detect column order from the metadata, in case PBI re-orders.
  const columns = dataView.table?.columns ?? [];
  let idIdx = 0;
  let hexIdx = -1;
  columns.forEach((col, i) => {
    if (col?.roles?.["elementIds"]) idIdx = i;
    if (col?.roles?.["colorHex"]) hexIdx = i;
  });
  const hasColorColumn = hexIdx >= 0;

  // Highlights array (cross-filter). Not in the public type — runtime-populated.
  const tableAny = dataView.table as unknown as {
    highlights?: (powerbi.PrimitiveValue | null)[];
  };
  const highlights = tableAny?.highlights;

  const filteredIds: number[] = [];
  const highlightedIds: number[] = [];
  const filteredColored: ColoredRow[] = [];
  const highlightedColored: ColoredRow[] = [];

  rows.forEach((row, i) => {
    const id = row[idIdx];
    if (typeof id !== "number") return;
    filteredIds.push(id);
    const hex = hasColorColumn ? (row[hexIdx] as string | null | undefined) : undefined;
    if (hex && typeof hex === "string" && /^#?[0-9a-f]{6}([0-9a-f]{2})?$/i.test(hex)) {
      filteredColored.push({ id, hex });
    }
    if (highlights && highlights[i] != null) {
      highlightedIds.push(id);
      if (hex && typeof hex === "string" && /^#?[0-9a-f]{6}([0-9a-f]{2})?$/i.test(hex)) {
        highlightedColored.push({ id, hex });
      }
    }
  });

  return {
    filteredIds,
    highlightedIds,
    filteredColored,
    highlightedColored,
    hasColorColumn,
  };
}

// ─── HTTP helpers ───────────────────────────────────────────────────────────

interface PostResult {
  ok: boolean;
  status: number;
  body: any;
}

async function post(url: string, payload: any): Promise<PostResult> {
  try {
    const resp = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
      signal: AbortSignal.timeout(15000), // create-view can take longer
    });
    let body: any = null;
    try { body = await resp.json(); } catch { /* non-JSON response */ }
    return { ok: resp.ok, status: resp.status, body };
  } catch {
    return { ok: false, status: 0, body: null };
  }
}

async function checkConnection(): Promise<boolean> {
  try {
    const resp = await fetch(PBI_SELECT_URL, {
      method: "OPTIONS",
      signal: AbortSignal.timeout(3000),
    });
    return resp.ok;
  } catch {
    return false;
  }
}

// ─── React component ────────────────────────────────────────────────────────

type Feedback =
  | { kind: "sent"; count: number }
  | { kind: "colored"; count: number }
  | { kind: "reset"; count: number }
  | { kind: "view"; name: string };

interface Props {
  filteredIds: number[];
  highlightedIds: number[];
  filteredColored: ColoredRow[];
  highlightedColored: ColoredRow[];
  hasColorColumn: boolean;
  connected: boolean;
  feedback: Feedback | null;
  strings: Strings;
  onSelect: (ids: number[], action: "select" | "isolate") => void;
  onColor: (items: ColoredRow[]) => void;
  onReset: () => void;
  onCreateView: (ids: number[]) => void;
}

function SelectionPanel({
  filteredIds,
  highlightedIds,
  filteredColored,
  highlightedColored,
  hasColorColumn,
  connected,
  feedback,
  strings: t,
  onSelect,
  onColor,
  onReset,
  onCreateView,
}: Props) {
  // Cross-filter active → use highlighted; otherwise → all filtered rows
  const useHighlighted = highlightedIds.length > 0;
  const activeIds = useHighlighted ? highlightedIds : filteredIds;
  const activeLabel = useHighlighted ? t.highlighted : t.filtered;
  const activeColored = useHighlighted ? highlightedColored : filteredColored;

  const canSelect = connected && activeIds.length > 0;
  const canColor = connected && hasColorColumn && activeColored.length > 0;
  const canReset = connected;
  const canCreateView = connected && activeIds.length > 0;

  // Status pill ("Server running" style from RevitCortex Settings)
  const statusBg = connected ? PALETTE.successBg : PALETTE.bgSubtle;
  const statusBorder = connected ? PALETTE.successText : PALETTE.border;
  const statusDot = connected ? PALETTE.activeGreen : PALETTE.inactiveGray;
  const statusText = connected ? t.connected : t.notConnected;

  return (
    <div
      style={{
        fontFamily: "Segoe UI, sans-serif",
        color: PALETTE.textPrimary,
        padding: 10,
        display: "flex",
        flexDirection: "column",
        gap: 8,
        height: "100%",
        boxSizing: "border-box",
        overflow: "auto",
      }}
    >
      {/* Title */}
      <div
        style={{
          fontSize: 14,
          fontWeight: 600,
          color: PALETTE.teal,
          letterSpacing: 0.2,
        }}
      >
        {t.title}
      </div>

      {/* Status pill */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 8,
          padding: "6px 10px",
          background: statusBg,
          border: `1px solid ${statusBorder}`,
          borderRadius: 4,
          fontSize: 11,
        }}
      >
        <span
          style={{
            width: 8,
            height: 8,
            borderRadius: "50%",
            backgroundColor: statusDot,
            display: "inline-block",
          }}
        />
        <span style={{ color: connected ? PALETTE.successText : PALETTE.textMuted }}>
          {statusText}
        </span>
      </div>

      {/* Primary action */}
      <button
        onClick={() => onSelect(activeIds, "select")}
        disabled={!canSelect}
        style={primaryBtnStyle(canSelect)}
        title={
          canSelect
            ? t.hintSelect(activeIds.length, activeLabel)
            : connected
              ? t.nothingToSelect
              : t.hintConnect
        }
      >
        <div style={{ fontSize: 16, fontWeight: 600 }}>{t.selectButton}</div>
        <div style={{ fontSize: 11, opacity: 0.9, marginTop: 2 }}>
          {activeIds.length} {activeLabel}
          {useHighlighted && filteredIds.length !== highlightedIds.length
            ? ` · ${filteredIds.length} ${t.totals}`
            : ""}
        </div>
      </button>

      {/* Secondary actions row 1: Isolate + Color */}
      <div style={{ display: "flex", gap: 6 }}>
        <button
          onClick={() => onSelect(activeIds, "isolate")}
          disabled={!canSelect}
          style={{ ...secondaryBtnStyle(canSelect), flex: 1 }}
          title={t.isolateHint}
        >
          {t.isolateButton}
        </button>
        <button
          onClick={() => onColor(activeColored)}
          disabled={!canColor}
          style={{ ...secondaryBtnStyle(canColor), flex: 1 }}
          title={hasColorColumn ? t.colorHint : t.noColorColumnHint}
        >
          {t.colorButton}
          {hasColorColumn && activeColored.length > 0 ? ` (${activeColored.length})` : ""}
        </button>
      </div>

      {/* Secondary actions row 2: Create view + Reset overrides */}
      <div style={{ display: "flex", gap: 6 }}>
        <button
          onClick={() => onCreateView(activeIds)}
          disabled={!canCreateView}
          style={{ ...secondaryBtnStyle(canCreateView), flex: 1 }}
          title={t.createViewHint}
        >
          {t.createViewButton}
        </button>
        <button
          onClick={() => onReset()}
          disabled={!canReset}
          style={{ ...resetBtnStyle(canReset), flex: 1 }}
          title={t.resetHint}
        >
          {t.resetButton}
        </button>
      </div>

      {/* Feedback */}
      {feedback && (
        <div
          style={{
            fontSize: 11,
            color: PALETTE.successText,
            background: PALETTE.successBg,
            border: `1px solid ${PALETTE.successText}33`,
            borderRadius: 4,
            padding: "4px 8px",
          }}
        >
          {feedback.kind === "sent" ? t.sent(feedback.count) :
           feedback.kind === "colored" ? t.colored(feedback.count) :
           feedback.kind === "reset" ? t.reset(feedback.count) :
           t.viewCreated(feedback.name)}
        </div>
      )}
    </div>
  );
}

function primaryBtnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: "10px 12px",
    cursor: enabled ? "pointer" : "not-allowed",
    background: enabled ? PALETTE.teal : PALETTE.bgSubtle,
    color: enabled ? "#fff" : PALETTE.textMuted,
    border: enabled ? `1px solid ${PALETTE.tealDark}` : `1px solid ${PALETTE.border}`,
    borderRadius: 4,
    width: "100%",
    textAlign: "left",
    transition: "background 0.15s ease",
  };
}

function secondaryBtnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: "6px 10px",
    fontSize: 12,
    cursor: enabled ? "pointer" : "not-allowed",
    background: "transparent",
    color: enabled ? PALETTE.teal : PALETTE.textMuted,
    border: `1px solid ${enabled ? PALETTE.teal : PALETTE.border}`,
    borderRadius: 3,
  };
}

function resetBtnStyle(enabled: boolean): React.CSSProperties {
  // Reset uses amber tint to signal "undo / destructive-ish" without being scary
  return {
    padding: "6px 10px",
    fontSize: 12,
    cursor: enabled ? "pointer" : "not-allowed",
    background: "transparent",
    color: enabled ? PALETTE.amber : PALETTE.textMuted,
    border: `1px solid ${enabled ? PALETTE.amber : PALETTE.border}`,
    borderRadius: 3,
  };
}

// ─── IVisual implementation ─────────────────────────────────────────────────

export class RevitCortexSelectionVisual implements IVisual {
  private readonly target: HTMLElement;
  private readonly strings: Strings;
  private filteredIds: number[] = [];
  private highlightedIds: number[] = [];
  private filteredColored: ColoredRow[] = [];
  private highlightedColored: ColoredRow[] = [];
  private hasColorColumn: boolean = false;
  private connected: boolean = false;
  private feedback: Feedback | null = null;
  private checkTimer: ReturnType<typeof setInterval> | null = null;
  private feedbackTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(options: VisualConstructorOptions) {
    this.target = options.element;
    this.strings = STRINGS[detectLang(options.host)];
    this.startConnectionChecker();
  }

  private startConnectionChecker(): void {
    const check = async () => {
      const ok = await checkConnection();
      if (ok !== this.connected) {
        this.connected = ok;
        this.render();
      }
    };
    check();
    this.checkTimer = setInterval(check, CHECK_INTERVAL_MS);
  }

  public update(options: VisualUpdateOptions): void {
    const dv = options.dataViews?.[0];
    if (!dv) {
      this.filteredIds = [];
      this.highlightedIds = [];
      this.filteredColored = [];
      this.highlightedColored = [];
      this.hasColorColumn = false;
    } else {
      const data = extractData(dv);
      this.filteredIds = data.filteredIds;
      this.highlightedIds = data.highlightedIds;
      this.filteredColored = data.filteredColored;
      this.highlightedColored = data.highlightedColored;
      this.hasColorColumn = data.hasColorColumn;
    }
    this.render();
  }

  private showFeedback(f: Feedback): void {
    this.feedback = f;
    this.render();
    if (this.feedbackTimer !== null) clearTimeout(this.feedbackTimer);
    this.feedbackTimer = setTimeout(() => {
      this.feedback = null;
      this.render();
    }, 3000);
  }

  private async onSelect(ids: number[], action: "select" | "isolate"): Promise<void> {
    const r = await post(PBI_SELECT_URL, { elementIds: ids, action });
    if (r.ok && r.body?.success) {
      this.showFeedback({ kind: "sent", count: ids.length });
    } else {
      this.connected = await checkConnection();
      this.render();
    }
  }

  private async onColor(items: ColoredRow[]): Promise<void> {
    const r = await post(PBI_COLOR_URL, { items });
    if (r.ok && r.body?.success) {
      const count = parseInt(r.body.validated ?? `${items.length}`, 10) || items.length;
      this.showFeedback({ kind: "colored", count });
    } else {
      this.connected = await checkConnection();
      this.render();
    }
  }

  private async onReset(): Promise<void> {
    const r = await post(PBI_RESET_URL, {});
    if (r.ok && r.body?.success) {
      const count = parseInt(r.body.validated ?? "0", 10) || 0;
      this.showFeedback({ kind: "reset", count });
    } else {
      this.connected = await checkConnection();
      this.render();
    }
  }

  private async onCreateView(ids: number[]): Promise<void> {
    const r = await post(PBI_CREATE_VIEW_URL, { elementIds: ids });
    if (r.ok && r.body?.success) {
      const name = String(r.body.validated ?? "");
      this.showFeedback({ kind: "view", name });
    } else {
      this.connected = await checkConnection();
      this.render();
    }
  }

  private render(): void {
    ReactDOM.render(
      React.createElement(SelectionPanel, {
        filteredIds: this.filteredIds,
        highlightedIds: this.highlightedIds,
        filteredColored: this.filteredColored,
        highlightedColored: this.highlightedColored,
        hasColorColumn: this.hasColorColumn,
        connected: this.connected,
        feedback: this.feedback,
        strings: this.strings,
        onSelect: (ids, action) => { void this.onSelect(ids, action); },
        onColor: (items) => { void this.onColor(items); },
        onReset: () => { void this.onReset(); },
        onCreateView: (ids) => { void this.onCreateView(ids); },
      }),
      this.target
    );
  }

  public destroy(): void {
    if (this.checkTimer !== null) {
      clearInterval(this.checkTimer);
      this.checkTimer = null;
    }
    if (this.feedbackTimer !== null) {
      clearTimeout(this.feedbackTimer);
      this.feedbackTimer = null;
    }
    ReactDOM.unmountComponentAtNode(this.target);
  }
}
