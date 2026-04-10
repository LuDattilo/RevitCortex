import { randomBytes } from "crypto";

function formatDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}${m}${day}`;
}

const SESSION_ID = `${formatDate(new Date())}-${randomBytes(2).toString("hex")}`;

export function getSessionId(): string {
  return SESSION_ID;
}
