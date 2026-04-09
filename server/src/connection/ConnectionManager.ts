import { RevitClient } from "./RevitClient.js";
import { logError } from "../logging/logger.js";

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

  const client = new RevitClient("localhost", 8080);
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
