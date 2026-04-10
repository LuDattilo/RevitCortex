import { z } from "zod";

export const AnalyzeJournalInput = z.object({
  analysis_type: z.enum([
    "summary",
    "session_diagnostics",
    "memory_profile",
    "command_usage",
    "addin_audit",
    "transaction_log",
    "full",
  ]).describe("Type of analysis: summary, session_diagnostics, memory_profile, command_usage, addin_audit, transaction_log, or full (all combined)"),

  journal_path: z.string().optional()
    .describe("Explicit path to a journal file. If omitted, uses the most recent journal from the standard Revit folder"),

  revit_version: z.enum(["2023", "2024", "2025", "2026"]).optional().default("2025")
    .describe("Revit version used to locate the journal folder. Ignored if journal_path is provided"),

  last_n_sessions: z.number().int().min(1).max(20).optional().default(1)
    .describe("Number of recent journal sessions to analyze (default 1, max 20)"),
});
