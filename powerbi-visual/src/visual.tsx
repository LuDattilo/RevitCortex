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
  onSelect: (ids: number[], action: "select" | "isolate") => void;
}

function SelectionPanel({ filteredIds, highlightedIds, connected, onSelect }: Props) {
  const dotColor = connected ? "#107C10" : "#767676";
  const dotLabel = connected ? "Connesso a localhost:27016" : "RevitCortex non attivo";

  return (
    <div className="revitcortex-selection">
      <div style={{ fontWeight: 600, marginBottom: 4 }}>RevitCortex Selection</div>

      <button
        onClick={() => onSelect(filteredIds, "select")}
        disabled={filteredIds.length === 0}
        style={btnStyle(filteredIds.length > 0)}
      >
        → Seleziona filtrati ({filteredIds.length})
      </button>

      <button
        onClick={() => onSelect(highlightedIds, "select")}
        disabled={highlightedIds.length === 0}
        style={btnStyle(highlightedIds.length > 0)}
      >
        → Seleziona highlighted ({highlightedIds.length})
      </button>

      <div style={{ display: "flex", alignItems: "center", gap: 6, marginTop: 4 }}>
        <span
          style={{
            width: 8,
            height: 8,
            borderRadius: "50%",
            backgroundColor: dotColor,
            display: "inline-block",
          }}
        />
        <span style={{ fontSize: 11, color: "#555" }}>{dotLabel}</span>
      </div>
    </div>
  );
}

function btnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: "6px 10px",
    fontSize: 12,
    cursor: enabled ? "pointer" : "not-allowed",
    background: enabled ? "#0078d4" : "#e0e0e0",
    color: enabled ? "#fff" : "#999",
    border: "none",
    borderRadius: 3,
    width: "100%",
    textAlign: "left",
  };
}

// ─── IVisual implementation ─────────────────────────────────────────────────

export class RevitCortexSelectionVisual implements IVisual {
  private readonly target: HTMLElement;
  private filteredIds: number[] = [];
  private highlightedIds: number[] = [];
  private connected: boolean = false;
  private checkTimer: ReturnType<typeof setInterval> | null = null;

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
        onSelect: async (ids, action) => {
          const ok = await sendToRevit(ids, action);
          if (!ok) {
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
    ReactDOM.unmountComponentAtNode(this.target);
  }
}
