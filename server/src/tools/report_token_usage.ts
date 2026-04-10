import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ReportTokenUsageInput } from "../schemas/usage.js";
import { toolResponse, toolError } from "../logging/compactTool.js";
import { getUsageDatabase, queryUsage, aggregateUsage } from "../database/usageDb.js";
import { readApiUsage, aggregateApiUsage } from "../database/usageJsonl.js";
import { mkdirSync, writeFileSync, readFileSync, existsSync } from "fs";
import { join } from "path";
import { homedir } from "os";

const SETTINGS_PATH = join(homedir(), ".revitcortex", "settings.json");
const REPORTS_DIR = join(homedir(), ".revitcortex", "reports");

interface ModelPricing {
  inputPerMTok: number;
  outputPerMTok: number;
}

const DEFAULT_PRICING: Record<string, ModelPricing> = {
  "claude-sonnet-4-6": { inputPerMTok: 3.0, outputPerMTok: 15.0 },
  "claude-haiku-4-5": { inputPerMTok: 0.80, outputPerMTok: 4.0 },
  "claude-opus-4-6": { inputPerMTok: 15.0, outputPerMTok: 75.0 },
};

function loadPricing(): Record<string, ModelPricing> {
  try {
    if (existsSync(SETTINGS_PATH)) {
      const settings = JSON.parse(readFileSync(SETTINGS_PATH, "utf-8"));
      if (settings.tokenPricing) return settings.tokenPricing;
    }
  } catch { /* use defaults */ }
  return DEFAULT_PRICING;
}

function computeDateRange(period: string, startDate?: string, endDate?: string): { start: string; end: string } {
  const now = new Date();
  const end = endDate ?? now.toISOString();

  switch (period) {
    case "day": {
      const start = new Date(now);
      start.setHours(0, 0, 0, 0);
      return { start: start.toISOString(), end };
    }
    case "week": {
      const start = new Date(now);
      start.setDate(start.getDate() - 7);
      return { start: start.toISOString(), end };
    }
    case "month": {
      const start = new Date(now);
      start.setDate(start.getDate() - 30);
      return { start: start.toISOString(), end };
    }
    case "custom":
      return { start: startDate ?? new Date(0).toISOString(), end };
    default:
      return { start: new Date(0).toISOString(), end };
  }
}

function estimateCost(tokens: number, model: string, direction: "input" | "output", pricing: Record<string, ModelPricing>): number {
  const p = pricing[model] ?? pricing["claude-sonnet-4-6"] ?? { inputPerMTok: 3.0, outputPerMTok: 15.0 };
  const rate = direction === "input" ? p.inputPerMTok : p.outputPerMTok;
  return (tokens / 1_000_000) * rate;
}

export function registerReportTokenUsageTool(server: McpServer): void {
  server.tool(
    "report_token_usage",
    "Generate token usage and cost reports. Shows which tools/categories consume the most tokens, with estimated API costs. Supports day/week/month/custom periods with CSV export.",
    ReportTokenUsageInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        await getUsageDatabase();
        const pricing = loadPricing();
        const range = computeDateRange(args.period, args.startDate, args.endDate);

        // MCP tool usage from SQLite
        const groupByCol = args.groupBy === "tool" ? "tool_name"
          : args.groupBy === "category" ? "tool_category"
          : args.groupBy === "session" ? "session_id"
          : "date";

        const mcpAgg = aggregateUsage({ startDate: range.start, endDate: range.end }, groupByCol as any);

        let totalEstimatedTokens = 0;
        let totalCalls = 0;
        let estimatedSourceCount = 0;

        const breakdown: Array<Record<string, unknown>> = [];

        for (const row of mcpAgg) {
          const tokens = Number(row.total_tokens) || 0;
          totalEstimatedTokens += tokens;
          totalCalls += Number(row.calls) || 0;
          estimatedSourceCount += Number(row.calls) || 0;
          const cost = estimateCost(Math.ceil(tokens * 0.7), "claude-sonnet-4-6", "input", pricing)
                     + estimateCost(Math.ceil(tokens * 0.3), "claude-sonnet-4-6", "output", pricing);
          breakdown.push({
            key: row.group_key ?? "unknown",
            source: "estimated",
            calls: Number(row.calls) || 0,
            tokens,
            errors: Number(row.errors) || 0,
            avgDurationMs: Math.round(Number(row.avg_duration_ms) || 0),
            cost_USD: Math.round(cost * 1000) / 1000,
          });
        }

        // API usage from JSONL
        let totalInputTokens = 0;
        let totalOutputTokens = 0;
        let actualSourceCount = 0;

        if (args.includeApiCalls) {
          const apiEntries = readApiUsage(range.start, range.end);
          actualSourceCount = apiEntries.length;

          if (apiEntries.length > 0) {
            const apiAgg = aggregateApiUsage(apiEntries, args.groupBy);
            for (const row of apiAgg) {
              totalInputTokens += row.input_tokens;
              totalOutputTokens += row.output_tokens;
              totalCalls += row.calls;
              const cost = estimateCost(row.input_tokens, "claude-sonnet-4-6", "input", pricing)
                         + estimateCost(row.output_tokens, "claude-sonnet-4-6", "output", pricing);
              breakdown.push({
                key: row.group_key,
                source: "actual",
                calls: row.calls,
                inputTokens: row.input_tokens,
                outputTokens: row.output_tokens,
                thinkingTokens: row.thinking_tokens,
                cost_USD: Math.round(cost * 1000) / 1000,
              });
            }
          }
        }

        // Sort breakdown by cost descending
        breakdown.sort((a, b) => ((b.cost_USD as number) || 0) - ((a.cost_USD as number) || 0));

        const totalCost = breakdown.reduce((sum, b) => sum + ((b.cost_USD as number) || 0), 0);

        const result: Record<string, unknown> = {
          period: `${range.start.slice(0, 10)} to ${range.end.slice(0, 10)}`,
          groupBy: args.groupBy,
          summary: {
            totalCalls,
            totalEstimatedTokens,
            totalInputTokens,
            totalOutputTokens,
            estimatedCost_USD: Math.round(totalCost * 1000) / 1000,
            sources: { estimated: estimatedSourceCount, actual: actualSourceCount },
          },
          breakdown: breakdown.slice(0, 50),
        };

        // CSV export
        if (args.exportCsv) {
          mkdirSync(REPORTS_DIR, { recursive: true });
          const csvName = `usage-${range.start.slice(0, 10)}-to-${range.end.slice(0, 10)}.csv`;
          const csvPath = join(REPORTS_DIR, csvName);

          const allRows = queryUsage({ startDate: range.start, endDate: range.end });
          const apiRows = args.includeApiCalls ? readApiUsage(range.start, range.end) : [];

          const csvLines = ["timestamp,tool_name,category,session_id,estimated_tokens,input_tokens,output_tokens,source,model,duration_ms,cost_usd"];

          for (const r of allRows) {
            const tokens = Number(r.estimated_tokens) || 0;
            const cost = estimateCost(Math.ceil(tokens * 0.7), "claude-sonnet-4-6", "input", pricing)
                       + estimateCost(Math.ceil(tokens * 0.3), "claude-sonnet-4-6", "output", pricing);
            csvLines.push(`${r.timestamp},${r.tool_name},${r.tool_category ?? ""},${r.session_id ?? ""},${tokens},,,${r.source},${r.model ?? ""},${r.duration_ms ?? ""},${cost.toFixed(4)}`);
          }

          for (const r of apiRows) {
            const cost = estimateCost(r.input_tokens, r.model ?? "claude-sonnet-4-6", "input", pricing)
                       + estimateCost(r.output_tokens, r.model ?? "claude-sonnet-4-6", "output", pricing);
            const tools = (r.tool_calls ?? []).join(";");
            csvLines.push(`${r.timestamp},${tools},API Call,${r.session_id ?? ""},,${r.input_tokens},${r.output_tokens},actual,${r.model ?? ""},${r.duration_ms ?? ""},${cost.toFixed(4)}`);
          }

          writeFileSync(csvPath, csvLines.join("\n"), "utf-8");
          result.csvPath = csvPath;
        }

        return toolResponse("report_token_usage", result, Date.now() - start, args);
      } catch (error) {
        return toolError("report_token_usage", error, Date.now() - start);
      }
    }
  );
}
