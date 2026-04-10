import { z } from "zod";

export const ReportTokenUsageInput = z.object({
  period: z.enum(["day", "week", "month", "custom"]).describe(
    "Time period for the report. 'day' = today, 'week' = last 7 days, 'month' = last 30 days, 'custom' = use startDate/endDate"
  ),
  startDate: z.string().optional().describe("Start date (ISO format, e.g. '2026-04-01') — required for 'custom' period"),
  endDate: z.string().optional().describe("End date (ISO format, e.g. '2026-04-10') — required for 'custom' period"),
  groupBy: z.enum(["tool", "category", "session", "day"]).default("tool").describe(
    "How to group results: by individual tool, by tool category, by session, or by day"
  ),
  includeApiCalls: z.boolean().default(true).describe("Include direct Anthropic API call data from the Revit chat panel"),
  exportCsv: z.boolean().default(false).describe("Generate a CSV file in ~/.revitcortex/reports/"),
});
