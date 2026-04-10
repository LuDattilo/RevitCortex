import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AnalyzeJournalInput } from "../schemas/journal.js";
import { findJournalFiles } from "../journal/locator.js";
import { parseJournal } from "../journal/parser.js";
import { runAnalysis } from "../journal/analyzers.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerAnalyzeJournalTool(server: McpServer): void {
  server.tool(
    "analyze_journal",
    "Analyze Revit journal files: session diagnostics, memory profiling, command usage, add-in audit, transaction log, or full analysis",
    AnalyzeJournalInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        // Locate journal files
        const paths = args.journal_path
          ? [args.journal_path]
          : findJournalFiles(args.revit_version ?? "2025", args.last_n_sessions ?? 1);

        const analysisType = args.analysis_type;

        if (paths.length === 1) {
          // Single session analysis
          const journal = parseJournal(paths[0]);
          const result = runAnalysis(journal, analysisType);
          return toolResponse("analyze_journal", {
            filePath: paths[0],
            analysisType,
            ...result as Record<string, unknown>,
          }, Date.now() - start, args);
        }

        // Multi-session analysis
        const sessions = paths.map((p) => {
          const journal = parseJournal(p);
          return {
            filePath: p,
            analysis: runAnalysis(journal, analysisType),
          };
        });

        return toolResponse("analyze_journal", {
          analysisType,
          sessionCount: sessions.length,
          sessions,
        }, Date.now() - start, args);
      } catch (error) {
        return toolError("analyze_journal", error, Date.now() - start);
      }
    }
  );
}
