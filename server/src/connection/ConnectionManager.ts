import { RevitClient } from "./RevitClient.js";
import { logError } from "../logging/logger.js";
import { readFileSync } from "fs";
import { join } from "path";
import { homedir } from "os";

function getPort(): number {
  const envPort = process.env.REVITCORTEX_PORT;
  if (envPort) {
    const p = parseInt(envPort, 10);
    if (p > 0 && p <= 65535) return p;
  }
  try {
    const settingsPath = join(homedir(), ".revitcortex", "settings.json");
    const settings = JSON.parse(readFileSync(settingsPath, "utf-8"));
    if (typeof settings.Port === "number" && settings.Port > 0 && settings.Port <= 65535)
      return settings.Port;
  } catch { /* fallback */ }
  return 8080;
}

const REVIT_PORT = getPort();

let connectionMutex: Promise<void> = Promise.resolve();

export async function withRevitConnection<T>(
  operation: (client: RevitClient) => Promise<T>
): Promise<T> {
  const previousMutex = connectionMutex;
  let releaseMutex: () => void;
  connectionMutex = new Promise<void>((resolve) => {
    releaseMutex = resolve;
  });
  await previousMutex;

  const client = new RevitClient("localhost", REVIT_PORT);
  try {
    await client.connect();
    return await operation(client);
  } catch (err) {
    logError("Revit connection error", err);
    throw err;
  } finally {
    client.disconnect();
    releaseMutex!();
  }
}
