interface ToolCallLog {
  tool: string;
  success: boolean;
  errorCode?: string;
  durationMs: number;
}

export function logToolCall(entry: ToolCallLog): void {
  const level = entry.success ? "info" : "error";
  process.stderr.write(
    JSON.stringify({
      level,
      timestamp: new Date().toISOString(),
      tool: entry.tool,
      success: entry.success,
      errorCode: entry.errorCode,
      durationMs: entry.durationMs,
    }) + "\n"
  );
}

export function logInfo(message: string): void {
  process.stderr.write(
    JSON.stringify({
      level: "info",
      timestamp: new Date().toISOString(),
      message,
    }) + "\n"
  );
}

export function logError(message: string, error?: unknown): void {
  process.stderr.write(
    JSON.stringify({
      level: "error",
      timestamp: new Date().toISOString(),
      message,
      error: error instanceof Error ? error.message : String(error),
    }) + "\n"
  );
}
