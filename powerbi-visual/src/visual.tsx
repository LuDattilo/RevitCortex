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

// ─── Palette ────────────────────────────────────────────────────────────────
// Neutral Microsoft Fluent / Power BI defaults. No product branding — the visual
// blends with whatever PBI theme the user has applied to the report.
const PALETTE = {
  accent:      "#0078D4", // Fluent communication blue (focus ring, primary action)
  accentDark:  "#106EBE", // hover/active darken
  bg:          "#FFFFFF",
  bgSubtle:    "#F3F2F1", // neutralLighter — button rest background
  bgHover:     "#E1DFDD", // neutralLight   — button hover
  bgActive:    "#C8C6C4", // neutralTertiaryAlt — button pressed
  border:      "#EDEBE9", // neutralLighterAlt — button outline at rest
  borderStrong:"#C8C6C4", // for clearer separators
  textPrimary: "#323130", // neutralPrimary
  textMuted:   "#605E5C", // neutralSecondary
  textDisabled:"#A19F9D", // neutralTertiary
  successBg:   "#DFF6DD",
  successText: "#107C10", // Fluent system green
  okDot:       "#107C10",
  offDot:      "#A19F9D",
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
    colorHint: "Apply color override on the active view, one color per distinct value in the mapped 'Color by' field",
    resetHint: "Reset all view overrides on the active view",
    createViewHint: "Create a new 3D view with a section box around the elements (added to Project Browser; current view is preserved)",
    noColorColumnHint: "Drop a categorical field into 'Color by' to enable this button",
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
    colorHint: "Applica un override colore sulla vista attiva, un colore per ogni valore distinto del campo mappato in 'Color by'",
    resetHint: "Rimuove tutti gli override grafici sulla vista attiva",
    createViewHint: "Crea una nuova vista 3D con section box attorno agli elementi (aggiunta al Project Browser; la vista corrente resta invariata)",
    noColorColumnHint: "Trascina un campo categorico in 'Color by' per abilitare questo pulsante",
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

// Categorical palette — Power BI default-ish colors, 16 stops. Reused as the
// user adds distinct values to the "Color by" role; we hash the value to pick
// a deterministic slot so identical values get identical colors across renders.
const CATEGORICAL_PALETTE: readonly string[] = [
  "#01B8AA", "#374649", "#FD625E", "#F2C80F", "#5F6B6D", "#8AD4EB",
  "#FE9666", "#A66999", "#3599B8", "#DFBFBF", "#4AC5BB", "#5F6B6D",
  "#FB8281", "#F4D25A", "#7F898A", "#A4DDEE",
] as const;

const HEX_RE = /^#?[0-9a-f]{6}([0-9a-f]{2})?$/i;

function normalizeHex(s: string): string {
  return s.startsWith("#") ? s : `#${s}`;
}

/** Stable 32-bit string hash (FNV-1a). Used to map a category value to a palette slot. */
function hashString(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
  }
  return h >>> 0;
}

function colorForCategory(value: string): string {
  return CATEGORICAL_PALETTE[hashString(value) % CATEGORICAL_PALETTE.length];
}

/**
 * Resolve a row value to a hex color.
 * - If the raw value is already a valid #RRGGBB(AA) string, pass through.
 * - Otherwise treat as a categorical value and hash → palette slot.
 * - null/undefined/empty → null (no override for this row).
 */
function resolveColor(raw: powerbi.PrimitiveValue | null | undefined): string | null {
  if (raw == null) return null;
  const s = String(raw);
  if (s.length === 0) return null;
  if (HEX_RE.test(s)) return normalizeHex(s);
  return colorForCategory(s);
}

/**
 * Extract all the data we need from the table data view in a single pass.
 * Column order matches the `dataViewMappings.table.rows.select` declaration:
 *   [0] elementIds
 *   [1] colorBy (optional)
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

  const columns = dataView.table?.columns ?? [];
  let idIdx = 0;
  let colorByIdx = -1;
  columns.forEach((col, i) => {
    if (col?.roles?.["elementIds"]) idIdx = i;
    if (col?.roles?.["colorBy"]) colorByIdx = i;
  });
  const hasColorColumn = colorByIdx >= 0;

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
    const hex = hasColorColumn ? resolveColor(row[colorByIdx]) : null;
    if (hex) filteredColored.push({ id, hex });
    if (highlights && highlights[i] != null) {
      highlightedIds.push(id);
      if (hex) highlightedColored.push({ id, hex });
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

type BusyKind = "select" | "isolate" | "color" | "reset" | "view" | null;

interface Props {
  filteredIds: number[];
  highlightedIds: number[];
  filteredColored: ColoredRow[];
  highlightedColored: ColoredRow[];
  hasColorColumn: boolean;
  connected: boolean;
  feedback: Feedback | null;
  feedbackVisible: boolean;
  busy: BusyKind;
  strings: Strings;
  onSelect: (ids: number[], action: "select" | "isolate") => void;
  onColor: (items: ColoredRow[]) => void;
  onReset: () => void;
  onCreateView: (ids: number[]) => void;
}

// Keyframes for spinner + toast slide-in. Injected once into the host document.
const STYLE_ID = "revitcortex-visual-animations";
function ensureAnimationsInjected(): void {
  if (typeof document === "undefined") return;
  if (document.getElementById(STYLE_ID) != null) return;
  const style = document.createElement("style");
  style.id = STYLE_ID;
  style.textContent = `
    @keyframes rc-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
    @keyframes rc-toast-in {
      from { opacity: 0; transform: translateY(4px); }
      to   { opacity: 1; transform: translateY(0); }
    }
    @keyframes rc-toast-out {
      from { opacity: 1; transform: translateY(0); }
      to   { opacity: 0; transform: translateY(-2px); }
    }
  `;
  document.head.appendChild(style);
}

// Small inline spinner used in busy state. Inherits color via currentColor.
function Spinner({ size = 12 }: { size?: number }) {
  return (
    <span
      style={{
        display: "inline-block",
        width: size,
        height: size,
        borderRadius: "50%",
        border: `2px solid currentColor`,
        borderTopColor: "transparent",
        animation: "rc-spin 0.7s linear infinite",
        marginRight: 6,
        verticalAlign: "-2px",
      }}
    />
  );
}

type Variant = "primary" | "default";

interface ActionButtonProps {
  variant: Variant;
  label: React.ReactNode;
  sublabel?: React.ReactNode;
  title: string;
  enabled: boolean;
  busy: boolean;
  onClick: () => void;
  style?: React.CSSProperties;
}

function ActionButton({
  variant,
  label,
  sublabel,
  title,
  enabled,
  busy,
  onClick,
  style,
}: ActionButtonProps) {
  const [hover, setHover] = React.useState(false);
  const [active, setActive] = React.useState(false);
  const [focus, setFocus] = React.useState(false);

  const interactive = enabled && !busy;
  const computed = computeButtonStyle(variant, { enabled: interactive, hover, active, focus });

  return (
    <button
      onClick={interactive ? onClick : undefined}
      disabled={!interactive}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => { setHover(false); setActive(false); }}
      onMouseDown={() => setActive(true)}
      onMouseUp={() => setActive(false)}
      onFocus={() => setFocus(true)}
      onBlur={() => setFocus(false)}
      title={title}
      style={{ ...computed, ...style }}
    >
      {busy && <Spinner />}
      <span style={{ display: "inline-flex", flexDirection: "column", alignItems: "flex-start" }}>
        <span style={variant === "primary" ? { fontSize: 16, fontWeight: 600 } : undefined}>
          {label}
        </span>
        {sublabel && (
          <span style={{ fontSize: 11, opacity: 0.9, marginTop: 2 }}>{sublabel}</span>
        )}
      </span>
    </button>
  );
}

function SelectionPanel({
  filteredIds,
  highlightedIds,
  filteredColored,
  highlightedColored,
  hasColorColumn,
  connected,
  feedback,
  feedbackVisible,
  busy,
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

  const anyBusy = busy != null;
  const canSelect = connected && activeIds.length > 0 && !anyBusy;
  const canColor = connected && hasColorColumn && activeColored.length > 0 && !anyBusy;
  const canReset = connected && !anyBusy;
  const canCreateView = connected && activeIds.length > 0 && !anyBusy;

  // Minimal status — small dot + neutral text. No coloured pill, no inner title:
  // PBI users get the visual title from the host (Format → Title).
  const statusDot = connected ? PALETTE.okDot : PALETTE.offDot;
  const statusText = connected ? t.connected : t.notConnected;

  return (
    <div
      style={{
        fontFamily: "Segoe UI, sans-serif",
        color: PALETTE.textPrimary,
        padding: 8,
        display: "flex",
        flexDirection: "column",
        gap: 6,
        height: "100%",
        boxSizing: "border-box",
        overflow: "auto",
      }}
    >
      {/* Status line */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 6,
          fontSize: 11,
          color: PALETTE.textMuted,
          paddingBottom: 2,
        }}
      >
        <span
          style={{
            width: 6,
            height: 6,
            borderRadius: "50%",
            backgroundColor: statusDot,
            display: "inline-block",
          }}
        />
        <span>{statusText}</span>
      </div>

      {/* Primary action */}
      <ActionButton
        variant="primary"
        label={t.selectButton}
        sublabel={
          <>
            {activeIds.length} {activeLabel}
            {useHighlighted && filteredIds.length !== highlightedIds.length
              ? ` · ${filteredIds.length} ${t.totals}`
              : ""}
          </>
        }
        title={
          canSelect
            ? t.hintSelect(activeIds.length, activeLabel)
            : connected
              ? t.nothingToSelect
              : t.hintConnect
        }
        enabled={canSelect || busy === "select"}
        busy={busy === "select"}
        onClick={() => onSelect(activeIds, "select")}
      />

      {/* Secondary actions row 1: Isolate + Color */}
      <div style={{ display: "flex", gap: 6 }}>
        <ActionButton
          variant="default"
          label={t.isolateButton}
          title={t.isolateHint}
          enabled={canSelect || busy === "isolate"}
          busy={busy === "isolate"}
          onClick={() => onSelect(activeIds, "isolate")}
          style={{ flex: 1 }}
        />
        <ActionButton
          variant="default"
          label={
            <>
              {t.colorButton}
              {hasColorColumn && activeColored.length > 0 ? ` (${activeColored.length})` : ""}
            </>
          }
          title={hasColorColumn ? t.colorHint : t.noColorColumnHint}
          enabled={canColor || busy === "color"}
          busy={busy === "color"}
          onClick={() => onColor(activeColored)}
          style={{ flex: 1 }}
        />
      </div>

      {/* Secondary actions row 2: Create view + Reset overrides */}
      <div style={{ display: "flex", gap: 6 }}>
        <ActionButton
          variant="default"
          label={t.createViewButton}
          title={t.createViewHint}
          enabled={canCreateView || busy === "view"}
          busy={busy === "view"}
          onClick={() => onCreateView(activeIds)}
          style={{ flex: 1 }}
        />
        <ActionButton
          variant="default"
          label={t.resetButton}
          title={t.resetHint}
          enabled={canReset || busy === "reset"}
          busy={busy === "reset"}
          onClick={() => onReset()}
          style={{ flex: 1 }}
        />
      </div>

      {/* Feedback toast — slide-in on appear, fade-out before unmount */}
      {feedback && (
        <div
          style={{
            fontSize: 11,
            color: PALETTE.successText,
            background: PALETTE.successBg,
            border: `1px solid ${PALETTE.successText}33`,
            borderRadius: 2,
            padding: "4px 8px",
            animation: feedbackVisible
              ? "rc-toast-in 180ms ease-out"
              : "rc-toast-out 220ms ease-in forwards",
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

interface ButtonState { enabled: boolean; hover: boolean; active: boolean; focus: boolean; }

function computeButtonStyle(variant: Variant, s: ButtonState): React.CSSProperties {
  const base: React.CSSProperties = {
    display: "inline-flex",
    alignItems: "center",
    justifyContent: variant === "primary" ? "flex-start" : "center",
    cursor: s.enabled ? "pointer" : "not-allowed",
    borderRadius: 2, // Fluent uses sharp 2px corners
    transition: "background 120ms ease, border-color 120ms ease",
    outline: "none",
    boxShadow: s.focus && s.enabled
      ? `0 0 0 2px ${PALETTE.accent}40`
      : "none",
    userSelect: "none",
    fontFamily: "Segoe UI, sans-serif",
  };

  if (variant === "primary") {
    const bg = !s.enabled
      ? PALETTE.bgSubtle
      : s.active ? PALETTE.accentDark
      : s.hover  ? PALETTE.accentDark
      : PALETTE.accent;
    return {
      ...base,
      padding: "8px 12px",
      width: "100%",
      textAlign: "left",
      background: bg,
      color: s.enabled ? "#fff" : PALETTE.textDisabled,
      border: `1px solid ${s.enabled ? bg : PALETTE.border}`,
    };
  }

  // default — Fluent neutral button (rest grey, darker grey on hover/active)
  const bg = !s.enabled
    ? PALETTE.bgSubtle
    : s.active ? PALETTE.bgActive
    : s.hover  ? PALETTE.bgHover
    : PALETTE.bgSubtle;
  return {
    ...base,
    padding: "6px 10px",
    fontSize: 12,
    background: bg,
    color: s.enabled ? PALETTE.textPrimary : PALETTE.textDisabled,
    border: `1px solid ${s.enabled ? PALETTE.border : PALETTE.border}`,
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
  private feedbackVisible: boolean = false;
  private busy: BusyKind = null;
  private checkTimer: ReturnType<typeof setInterval> | null = null;
  private feedbackTimer: ReturnType<typeof setTimeout> | null = null;
  private feedbackFadeTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(options: VisualConstructorOptions) {
    this.target = options.element;
    this.strings = STRINGS[detectLang(options.host)];
    ensureAnimationsInjected();
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
    if (this.feedbackTimer !== null) clearTimeout(this.feedbackTimer);
    if (this.feedbackFadeTimer !== null) clearTimeout(this.feedbackFadeTimer);
    this.feedback = f;
    this.feedbackVisible = true;
    this.render();
    // Start fade-out 220ms before unmount so the CSS animation has time to play.
    this.feedbackFadeTimer = setTimeout(() => {
      this.feedbackVisible = false;
      this.render();
    }, 2780);
    this.feedbackTimer = setTimeout(() => {
      this.feedback = null;
      this.render();
    }, 3000);
  }

  private setBusy(kind: BusyKind): void {
    this.busy = kind;
    this.render();
  }

  private async runAction(kind: Exclude<BusyKind, null>, fn: () => Promise<void>): Promise<void> {
    if (this.busy != null) return; // one action at a time
    this.setBusy(kind);
    try {
      await fn();
    } finally {
      this.setBusy(null);
    }
  }

  private async onSelect(ids: number[], action: "select" | "isolate"): Promise<void> {
    return this.runAction(action === "isolate" ? "isolate" : "select", async () => {
      const r = await post(PBI_SELECT_URL, { elementIds: ids, action });
      if (r.ok && r.body?.success) {
        this.showFeedback({ kind: "sent", count: ids.length });
      } else {
        this.connected = await checkConnection();
      }
    });
  }

  private async onColor(items: ColoredRow[]): Promise<void> {
    return this.runAction("color", async () => {
      const r = await post(PBI_COLOR_URL, { items });
      if (r.ok && r.body?.success) {
        const count = parseInt(r.body.validated ?? `${items.length}`, 10) || items.length;
        this.showFeedback({ kind: "colored", count });
      } else {
        this.connected = await checkConnection();
      }
    });
  }

  private async onReset(): Promise<void> {
    return this.runAction("reset", async () => {
      const r = await post(PBI_RESET_URL, {});
      if (r.ok && r.body?.success) {
        const count = parseInt(r.body.validated ?? "0", 10) || 0;
        this.showFeedback({ kind: "reset", count });
      } else {
        this.connected = await checkConnection();
      }
    });
  }

  private async onCreateView(ids: number[]): Promise<void> {
    return this.runAction("view", async () => {
      const r = await post(PBI_CREATE_VIEW_URL, { elementIds: ids });
      if (r.ok && r.body?.success) {
        const name = String(r.body.validated ?? "");
        this.showFeedback({ kind: "view", name });
      } else {
        this.connected = await checkConnection();
      }
    });
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
        feedbackVisible: this.feedbackVisible,
        busy: this.busy,
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
    if (this.feedbackFadeTimer !== null) {
      clearTimeout(this.feedbackFadeTimer);
      this.feedbackFadeTimer = null;
    }
    ReactDOM.unmountComponentAtNode(this.target);
  }
}
