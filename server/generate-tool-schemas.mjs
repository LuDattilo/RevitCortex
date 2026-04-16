#!/usr/bin/env node
/**
 * DEPRECATED — delegates to the C# server edition.
 *
 * The TypeScript MCP server has been replaced by a pure C# implementation.
 * This shim exists only for backward compatibility with old scripts/CI.
 *
 * Use directly:
 *   node server/generate-tool-schemas-csharp.mjs
 */
import { spawn } from "child_process";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const target = join(__dirname, "generate-tool-schemas-csharp.mjs");

console.warn(
  "[DEPRECATED] generate-tool-schemas.mjs → forwarding to generate-tool-schemas-csharp.mjs"
);

const child = spawn(process.execPath, [target], { stdio: "inherit" });
child.on("close", (code) => process.exit(code ?? 0));
