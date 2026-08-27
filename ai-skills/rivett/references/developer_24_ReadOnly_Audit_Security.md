# Safety metadata, audit log, and sandbox

## Tool safety metadata

`[ToolSafety(readOnly, destructive)]` describes a tool for routing, auditing,
and agents. It is metadata in RiveTT; there is no settings-based read-only
profile and no `readOnlyMode` setting — any guidance mentioning one is stale.

It surfaces in responses as `execution.toolReadOnly` / `toolDestructive`,
alongside the session-wide `writesAllowed` (always true). The fields were named
`readOnly`/`destructive` until they were read as a server-wide lock, which led a
session to be treated as read-only when writes were always allowed. Keep names
and attributes aligned.

## Audit

Every routed call is appended to
`%LOCALAPPDATA%\RiveTT\audit.jsonl`. Preserve the router-wide backstop so
successes and failures are both logged, including duration and response size.
The router also records an output summary and derives the affected-element count
from structured counters/ID arrays; previews are logged with `mutated:false` in
their response contract.

## `send_code_to_revit`

`CodeSandbox.Validate` must run before compilation. It blocks filesystem and
network access, process spawning, registry access, native interop, reflection
emit, and related bypasses. The tool remains a last resort even though
RiveTT has no confirmation dialogs.

## Checks

- Safety attribute matches actual behavior.
- Every exception becomes a structured failure.
- Audit logging remains best-effort and non-crashing.
- Sandbox hardening tests remain enabled.
- Dedicated tools are preferred over arbitrary code.
