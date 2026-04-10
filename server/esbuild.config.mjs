import { build } from "esbuild";

await build({
  entryPoints: ["src/index.ts"],
  bundle: true,
  platform: "node",
  target: "node18",
  format: "esm",
  outfile: "build/index.js",
  banner: {
    js: [
      'import { createRequire as __createRequire } from "module";',
      'import { fileURLToPath as __fileURLToPath } from "url";',
      'import { dirname as __dirname_fn } from "path";',
      'const require = __createRequire(import.meta.url);',
      'const __filename = __fileURLToPath(import.meta.url);',
      'const __dirname = __dirname_fn(__filename);',
    ].join("\n"),
  },
  external: [
    "fs", "path", "os", "url", "module", "crypto", "events", "stream",
    "util", "net", "tls", "http", "https", "zlib", "buffer",
    "string_decoder", "child_process", "worker_threads", "node:*",
    "sql.js",
  ],
  sourcemap: false,
  minify: false,
});

console.log("Build complete: build/index.js");
