import * as React from "react";
import * as ReactDOM from "react-dom";
import powerbi from "powerbi-visuals-api";
import IVisual = powerbi.extensibility.visual.IVisual;
import VisualConstructorOptions = powerbi.extensibility.visual.VisualConstructorOptions;
import VisualUpdateOptions = powerbi.extensibility.visual.VisualUpdateOptions;

const PBI_SELECT_URL = "http://localhost:27016/pbi-select";
const CHECK_INTERVAL_MS = 30_000;

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
  onSelect: (ids: number[], action: "select" | "isolate") => void;
}

function SelectionPanel({
  filteredIds,
  highlightedIds,
  connected,
  lastSent,
  onSelect,
}: Props) {
  // The "active" set is what the primary button will send:
  // - if there are highlighted ids (cross-filter active) → use them
  // - otherwise → use all filtered ids
  const useHighlighted = highlightedIds.length > 0;
  const activeIds = useHighlighted ? highlightedIds : filteredIds;
  const activeLabel = useHighlighted ? "evidenziati" : "filtrati";

  const dotColor = connected ? "#107C10" : "#767676";
  const dotLabel = connected ? "Connesso a Revit" : "RevitCortex non attivo";

  const canSelect = connected && activeIds.length > 0;

  return (
    <div
      style={{
        fontFamily: "Segoe UI, sans-serif",
        padding: 8,
        display: "flex",
        flexDirection: "column",
        gap: 8,
      }}
    >
      {/* Header */}
      <div style={{ fontWeight: 600, fontSize: 13 }}>RevitCortex Selection</div>

      {/* Primary action — big button */}
      <button
        onClick={() => onSelect(activeIds, "select")}
        disabled={!canSelect}
        style={primaryBtnStyle(canSelect)}
        title={
          canSelect
            ? `Seleziona ${activeIds.length} elementi ${activeLabel} in Revit`
            : connected
              ? "Nessun elemento da selezionare"
              : "RevitCortex non attivo — avvia il server in Revit"
        }
      >
        <div style={{ fontSize: 18, fontWeight: 600 }}>
          Seleziona in Revit
        </div>
        <div style={{ fontSize: 12, opacity: 0.9, marginTop: 2 }}>
          {activeIds.length} {activeLabel}
          {useHighlighted && filteredIds.length !== highlightedIds.length
            ? ` · ${filteredIds.length} totali`
            : ""}
        </div>
      </button>

      {/* Secondary action — isolate */}
      <button
        onClick={() => onSelect(activeIds, "isolate")}
        disabled={!canSelect}
        style={secondaryBtnStyle(canSelect)}
        title="Isola gli elementi nella vista attiva (oltre alla selezione)"
      >
        Isola in Revit
      </button>

      {/* Feedback after last send */}
      {lastSent !== null && (
        <div style={{ fontSize: 11, color: "#107C10" }}>
          ✓ Inviati {lastSent} elementi
        </div>
      )}

      {/* Connection indicator */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 6,
          marginTop: "auto",
          paddingTop: 4,
          borderTop: "1px solid #eee",
        }}
      >
        <span
          style={{
            width: 8,
            height: 8,
            borderRadius: "50%",
            backgroundColor: dotColor,
            display: "inline-block",
          }}
        />
        <span style={{ fontSize: 11, color: "#666" }}>{dotLabel}</span>
      </div>
    </div>
  );
}

function primaryBtnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: "12px 14px",
    cursor: enabled ? "pointer" : "not-allowed",
    background: enabled ? "#0078d4" : "#e0e0e0",
    color: enabled ? "#fff" : "#999",
    border: "none",
    borderRadius: 4,
    width: "100%",
    textAlign: "left",
  };
}

function secondaryBtnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: "6px 10px",
    fontSize: 12,
    cursor: enabled ? "pointer" : "not-allowed",
    background: "transparent",
    color: enabled ? "#0078d4" : "#999",
    border: `1px solid ${enabled ? "#0078d4" : "#ccc"}`,
    borderRadius: 3,
    width: "100%",
  };
}

// ─── IVisual implementation ─────────────────────────────────────────────────

export class RevitCortexSelectionVisual implements IVisual {
  private readonly target: HTMLElement;
  private filteredIds: number[] = [];
  private highlightedIds: number[] = [];
  private connected: boolean = false;
  private lastSent: number | null = null;
  private checkTimer: ReturnType<typeof setInterval> | null = null;
  private feedbackTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(options: VisualConstructorOptions) {
    this.target = options.element;
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
        onSelect: async (ids, action) => {
          const ok = await sendToRevit(ids, action);
          if (ok) {
            // Show "Inviati N elementi" feedback for 3 seconds
            this.lastSent = ids.length;
            this.render();
            if (this.feedbackTimer !== null) clearTimeout(this.feedbackTimer);
            this.feedbackTimer = setTimeout(() => {
              this.lastSent = null;
              this.render();
            }, 3000);
          } else {
            // Re-check connection on send failure and re-render indicator
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
