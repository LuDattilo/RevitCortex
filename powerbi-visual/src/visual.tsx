import * as React from "react";
import * as ReactDOM from "react-dom";
import powerbi from "powerbi-visuals-api";
import IVisual = powerbi.extensibility.visual.IVisual;
import VisualConstructorOptions = powerbi.extensibility.visual.VisualConstructorOptions;
import VisualUpdateOptions = powerbi.extensibility.visual.VisualUpdateOptions;

const PBI_SELECT_URL = "http://localhost:27016/pbi-select";
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
  highlighted: string;     // "evidenziati" / "highlighted"
  filtered: string;        // "filtrati" / "filtered"
  totals: string;          // "totali" / "total"
  sent: (n: number) => string;
  connected: string;
  notConnected: string;
  nothingToSelect: string;
  hintConnect: string;
  hintSelect: (n: number, label: string) => string;
  isolateHint: string;
}

const STRINGS: Record<Lang, Strings> = {
  en: {
    title: "RevitCortex Selection",
    selectButton: "Select in Revit",
    isolateButton: "Isolate in Revit",
    highlighted: "highlighted",
    filtered: "filtered",
    totals: "total",
    sent: (n) => `✓ Sent ${n} element${n === 1 ? "" : "s"}`,
    connected: "Connected to Revit",
    notConnected: "RevitCortex not running",
    nothingToSelect: "Nothing to select",
    hintConnect: "RevitCortex not active — start the server in Revit",
    hintSelect: (n, label) => `Select ${n} ${label} element${n === 1 ? "" : "s"} in Revit`,
    isolateHint: "Isolate the elements in the active view (in addition to selection)",
  },
  it: {
    title: "RevitCortex Selection",
    selectButton: "Seleziona in Revit",
    isolateButton: "Isola in Revit",
    highlighted: "evidenziati",
    filtered: "filtrati",
    totals: "totali",
    sent: (n) => `✓ Inviati ${n} element${n === 1 ? "o" : "i"}`,
    connected: "Connesso a Revit",
    notConnected: "RevitCortex non attivo",
    nothingToSelect: "Nessun elemento da selezionare",
    hintConnect: "RevitCortex non attivo — avvia il server in Revit",
    hintSelect: (n, label) => `Seleziona ${n} elementi ${label} in Revit`,
    isolateHint: "Isola gli elementi nella vista attiva (oltre alla selezione)",
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

function getFilteredIds(dataView: powerbi.DataView): number[] {
  const rows = dataView.table?.rows ?? [];
  return rows.map(row => row[0] as number).filter(id => typeof id === "number");
}

function getHighlightedIds(dataView: powerbi.DataView): number[] {
  const rows = dataView.table?.rows ?? [];
  // powerbi-visuals-api does not type `highlights` on DataViewTable,
  // but Power BI Desktop populates it at runtime when supportsHighlight=true.
  // Cast through unknown to access it safely.
  const tableAny = dataView.table as unknown as { highlights?: (powerbi.PrimitiveValue | null)[] };
  const highlights = tableAny?.highlights;
  if (!highlights) return [];
  // Use loose inequality (!= null) to catch both null (not highlighted) and
  // undefined (highlights array not populated / no cross-filter active).
  return rows
    .filter((_, i) => highlights[i] != null)
    .map(row => row[0] as number)
    .filter(id => typeof id === "number");
}

// ─── HTTP helpers ───────────────────────────────────────────────────────────

async function sendToRevit(
  elementIds: number[],
  action: "select" | "isolate"
): Promise<boolean> {
  try {
    const resp = await fetch(PBI_SELECT_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ elementIds, action }),
      signal: AbortSignal.timeout(5000),
    });
    return resp.ok;
  } catch {
    return false;
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

interface Props {
  filteredIds: number[];
  highlightedIds: number[];
  connected: boolean;
  lastSent: number | null;
  strings: Strings;
  onSelect: (ids: number[], action: "select" | "isolate") => void;
}

function SelectionPanel({
  filteredIds,
  highlightedIds,
  connected,
  lastSent,
  strings: t,
  onSelect,
}: Props) {
  // Cross-filter active → use highlighted; otherwise → all filtered rows
  const useHighlighted = highlightedIds.length > 0;
  const activeIds = useHighlighted ? highlightedIds : filteredIds;
  const activeLabel = useHighlighted ? t.highlighted : t.filtered;

  const canSelect = connected && activeIds.length > 0;

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
        gap: 10,
        height: "100%",
        boxSizing: "border-box",
      }}
    >
      {/* Title — teal like the Settings header */}
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

      {/* Secondary action */}
      <button
        onClick={() => onSelect(activeIds, "isolate")}
        disabled={!canSelect}
        style={secondaryBtnStyle(canSelect)}
        title={t.isolateHint}
      >
        {t.isolateButton}
      </button>

      {/* Feedback */}
      {lastSent !== null && (
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
          {t.sent(lastSent)}
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
    width: "100%",
  };
}

// ─── IVisual implementation ─────────────────────────────────────────────────

export class RevitCortexSelectionVisual implements IVisual {
  private readonly target: HTMLElement;
  private readonly strings: Strings;
  private filteredIds: number[] = [];
  private highlightedIds: number[] = [];
  private connected: boolean = false;
  private lastSent: number | null = null;
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
    check(); // immediate check on mount
    this.checkTimer = setInterval(check, CHECK_INTERVAL_MS);
  }

  public update(options: VisualUpdateOptions): void {
    const dv = options.dataViews?.[0];
    if (!dv) {
      this.filteredIds = [];
      this.highlightedIds = [];
    } else {
      this.filteredIds = getFilteredIds(dv);
      this.highlightedIds = getHighlightedIds(dv);
    }
    this.render();
  }

  private render(): void {
    ReactDOM.render(
      React.createElement(SelectionPanel, {
        filteredIds: this.filteredIds,
        highlightedIds: this.highlightedIds,
        connected: this.connected,
        lastSent: this.lastSent,
        strings: this.strings,
        onSelect: async (ids, action) => {
          const ok = await sendToRevit(ids, action);
          if (ok) {
            this.lastSent = ids.length;
            this.render();
            if (this.feedbackTimer !== null) clearTimeout(this.feedbackTimer);
            this.feedbackTimer = setTimeout(() => {
              this.lastSent = null;
              this.render();
            }, 3000);
          } else {
            this.connected = await checkConnection();
            this.render();
          }
        },
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
