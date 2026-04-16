#!/usr/bin/env node
/**
 * RevitCortex — Tool Schema Generator (C# Server Edition)
 * =========================================================
 * Spawns RevitCortex.Server as a child process and queries it via the
 * MCP stdio transport (newline-delimited JSON-RPC, per MCP spec 2024-11-05).
 *
 * Why this works without Revit running:
 *   - RevitConnectionManager connects lazily (only when a tool executes)
 *   - tools/list is a pure MCP introspection call — no Revit I/O
 *   - Program.cs routes ALL logs to stderr → stdout is exclusively JSON-RPC
 *
 * Usage:   node server/generate-tool-schemas-csharp.mjs
 * Output:  tool-schemas.txt  (project root)
 */

import { spawn }       from "child_process";
import { writeFileSync, existsSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath }  from "url";

const __dirname    = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT    = join(__dirname, "..");
const OUTPUT_PATH  = join(REPO_ROOT, "tool-schemas.txt");
const SERVER_PROJ  = join(REPO_ROOT, "src", "RevitCortex.Server", "RevitCortex.Server.csproj");

const STARTUP_GRACE_MS = 2_000;   // ms to wait after spawning before sending MCP messages
const RESPONSE_TIMEOUT = 30_000;  // ms total timeout for tools/list response

// ── Step 1: verify project exists ────────────────────────────────────────────
if (!existsSync(SERVER_PROJ)) {
  console.error(`ERROR: Server project not found at ${SERVER_PROJ}`);
  process.exit(1);
}

// ── Step 2: build ─────────────────────────────────────────────────────────────
process.stdout.write("Building RevitCortex.Server ... ");

const build = spawn(
  "dotnet",
  ["build", SERVER_PROJ, "--verbosity", "quiet"],
  { stdio: ["ignore", "pipe", "pipe"] }
);

let buildErr = "";
build.stdout.on("data", () => {});                   // quiet
build.stderr.on("data", d => (buildErr += d));

build.on("close", code => {
  if (code !== 0) {
    console.error("FAILED\n" + buildErr);
    process.exit(1);
  }
  console.log("OK");
  runServer();
});

// ── Step 3: spawn server & query ──────────────────────────────────────────────
function runServer() {
  console.log("Spawning RevitCortex.Server ...");

  /**
   * dotnet run --no-build: reuses the artifact from the build step above.
   * stderr is inherited → server startup logs appear in the terminal for
   * diagnostics but do NOT pollute stdout (MCP protocol channel).
   */
  const server = spawn(
    "dotnet",
    ["run", "--project", SERVER_PROJ, "--no-build"],
    { stdio: ["pipe", "pipe", "inherit"] }   // stdin/stdout piped, stderr visible
  );

  let lineBuffer = "";
  let toolsResponse = null;
  let initAcknowledged = false;

  // ── MCP message receiver ────────────────────────────────────────────────
  server.stdout.on("data", data => {
    lineBuffer += data.toString();
    const lines = lineBuffer.split("\n");
    lineBuffer = lines.pop() ?? "";           // keep partial last line

    for (const line of lines) {
      const trimmed = line.trim();
      if (!trimmed || !trimmed.startsWith("{")) continue;

      let msg;
      try { msg = JSON.parse(trimmed); } catch { continue; }

      // id=0 → initialize response
      if (msg.id === 0 && !initAcknowledged) {
        initAcknowledged = true;
        console.log("  ✓ initialize acknowledged — requesting tools/list ...");
        sendMsg(server, { jsonrpc: "2.0", id: 1, method: "tools/list", params: {} });
      }

      // id=1 → tools/list response
      if (msg.id === 1 && msg.result?.tools) {
        toolsResponse = msg.result.tools;
        console.log(`  ✓ Received ${toolsResponse.length} tools`);
        shutdown(server, 0);
      }
    }
  });

  // ── Timeout guard ────────────────────────────────────────────────────────
  const timer = setTimeout(() => {
    console.error(`\nERROR: Timed out after ${RESPONSE_TIMEOUT / 1000}s waiting for tools/list`);
    shutdown(server, 1);
  }, RESPONSE_TIMEOUT);

  server.on("close", () => {
    clearTimeout(timer);
    if (toolsResponse) {
      writeSchemas(toolsResponse);
    } else {
      console.error("ERROR: Server exited before returning tools list");
      process.exit(1);
    }
  });

  // ── Send initialize after startup grace period ───────────────────────────
  setTimeout(() => {
    console.log("  Sending initialize ...");
    sendMsg(server, {
      jsonrpc: "2.0",
      id: 0,
      method: "initialize",
      params: {
        protocolVersion: "2024-11-05",
        capabilities:    {},
        clientInfo:      { name: "schema-gen-csharp", version: "1.0.0" },
      },
    });
  }, STARTUP_GRACE_MS);
}

// ── Step 4: write tool-schemas.txt ────────────────────────────────────────────
function writeSchemas(tools) {
  const lines = tools
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(tool => {
      const schema   = tool.inputSchema ?? {};
      const props    = schema.properties ?? {};
      const required = new Set(schema.required ?? []);

      const params = Object.entries(props)
        .map(([key, def]) => {
          let sig;

          if (def.type === "object" && def.properties) {
            // Inline object shape
            const inner = Object.entries(def.properties)
              .map(([k, v]) => `${k}:${v.type ?? "?"}`)
              .join(",");
            sig = `${key}:{${inner}}`;
          } else if (Array.isArray(def.enum)) {
            sig = `${key}:${def.enum.join("|")}`;
          } else {
            sig = `${key}:${def.type ?? "?"}`;
          }

          if (required.has(key)) sig += "!";
          return sig;
        })
        .join(", ");

      return `${tool.name}(${params})`;
    });

  writeFileSync(OUTPUT_PATH, lines.join("\n") + "\n", "utf8");
  console.log(`\n✓ Generated ${OUTPUT_PATH}`);
  console.log(`  ${lines.length} tools, ${Buffer.byteLength(lines.join("\n"), "utf8")} bytes`);
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function sendMsg(child, obj) {
  child.stdin.write(JSON.stringify(obj) + "\n");
}

function shutdown(child, exitCode) {
  try { child.stdin.end(); child.kill(); } catch {}
  if (exitCode !== 0) process.exit(exitCode);
}
