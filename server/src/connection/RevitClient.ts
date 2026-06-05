import { Socket } from "net";

export class RevitClient {
  private socket: Socket;
  private responseCallbacks: Map<string, (data: string) => void> = new Map();
  private timeouts: Map<string, NodeJS.Timeout> = new Map();
  private buffer: string = "";
  private requestCounter: number = 0;

  constructor(
    private host: string,
    private port: number
  ) {
    this.socket = new Socket();
  }

  connect(): Promise<void> {
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("Connection timed out")), 5000);
      this.socket.on("connect", () => {
        clearTimeout(timeout);
        // H12: once connected, the connect-phase error listener becomes a no-op (its
        // Promise is settled). Install persistent error/close handlers so a mid-command
        // socket failure (Revit crash, TCP reset) rejects the pending sendCommand
        // immediately instead of leaving it hung for the full 5-minute timeout — which
        // also holds the connection mutex and freezes every other tool call.
        this.socket.on("error", (err) => this.failAllPending(err));
        this.socket.on("close", () =>
          this.failAllPending(new Error("Connection to Revit closed unexpectedly")));
        resolve();
      });
      this.socket.on("error", (err) => {
        clearTimeout(timeout);
        reject(err);
      });
      this.socket.on("data", (data) => this.onData(data));
      this.socket.connect(this.port, this.host);
    });
  }

  /** Reject every in-flight command and clear its timeout (H12). */
  private failAllPending(err: Error): void {
    for (const timeout of this.timeouts.values()) clearTimeout(timeout);
    this.timeouts.clear();
    const callbacks = Array.from(this.responseCallbacks.values());
    this.responseCallbacks.clear();
    for (const cb of callbacks) {
      // Each callback parses the line and resolves/rejects its Promise; feed it a
      // JSON-RPC error envelope so it rejects rather than hangs.
      try {
        cb(JSON.stringify({ jsonrpc: "2.0", error: { message: err.message } }));
      } catch {
        // ignore — the Promise is being torn down anyway
      }
    }
  }

  disconnect(): void {
    this.socket.destroy();
    for (const timeout of this.timeouts.values()) clearTimeout(timeout);
    this.timeouts.clear();
    this.responseCallbacks.clear();
  }

  sendCommand(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    return new Promise((resolve, reject) => {
      const id = String(++this.requestCounter);
      const request = { jsonrpc: "2.0", method, params, id };

      this.responseCallbacks.set(id, (responseJson) => {
        try {
          const response = JSON.parse(responseJson);
          if (response.error) {
            reject(new Error(response.error.message || "Unknown Revit error"));
          } else {
            resolve(response.result);
          }
        } catch (e) {
          reject(e);
        }
      });

      const timeout = setTimeout(() => {
        this.responseCallbacks.delete(id);
        this.timeouts.delete(id);
        reject(new Error(`Command timed out after 5 minutes: ${method}`));
        this.socket.destroy();
      }, 300_000);
      this.timeouts.set(id, timeout);

      this.socket.write(JSON.stringify(request) + "\n");
    });
  }

  private onData(data: Buffer): void {
    this.buffer += data.toString();
    const lines = this.buffer.split("\n");
    this.buffer = lines.pop() || "";
    for (const line of lines) {
      if (!line.trim()) continue;
      try {
        const parsed = JSON.parse(line);
        const id = parsed.id;
        if (id && this.responseCallbacks.has(id)) {
          const timeout = this.timeouts.get(id);
          if (timeout) clearTimeout(timeout);
          this.timeouts.delete(id);
          const cb = this.responseCallbacks.get(id)!;
          this.responseCallbacks.delete(id);
          cb(line);
        }
      } catch {
        // skip malformed lines
      }
    }
  }
}
