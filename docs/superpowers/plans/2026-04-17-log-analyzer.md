# RevitCortex Log Analyzer — Implementation Plan

> **Execute with:** `superpowers:executing-plans` o subagent-driven.

**Goal:** CLI Python che prende in input uno ZIP di bug report (da `SendSupportReport`) e genera un **prompt markdown strutturato** da passare a una nuova sessione Claude Code per far implementare il fix.

**Principio architetturale:** l'app NON è una dashboard. È un **preprocessore** che comprime rumore (500 righe JSONL) in segnale (1 markdown denso). Claude Code è il "consumer" finale.

**Repo target:** nuovo repo privato `LuDattilo/revitcortex-log-analyzer`
**Stack:** Python 3.11+ (solo standard library + `click` per CLI)
**Output:** file `.md` in `./reports/<bug-id>/prompt.md` + `./reports/<bug-id>/raw/` con gli artefatti decompressi

---

## File Structure

```
revitcortex-log-analyzer/
├── README.md
├── pyproject.toml              # hatchling build + click dep
├── .gitignore                  # ignora reports/ e virtualenv
├── analyze.py                  # entry point CLI — thin wrapper
├── src/
│   └── rclog/
│       ├── __init__.py
│       ├── cli.py              # click commands (analyze, batch, serve)
│       ├── extractor.py        # unzip + find files (audit.jsonl, context.txt, journal, settings.json)
│       ├── parser.py           # parse JSONL streams in dataclasses
│       ├── diagnosis.py        # heuristics: classifica errori, trova pattern, ipotizza file sospetti
│       ├── prompt_builder.py   # assembla il markdown output
│       └── templates/
│           └── prompt.md.j2    # template Jinja-like (o plain string format)
├── tests/
│   ├── fixtures/
│   │   └── sample-bugreport.zip # uno ZIP generato dal tuo SendSupportReport
│   ├── test_extractor.py
│   ├── test_parser.py
│   ├── test_diagnosis.py
│   └── test_prompt_builder.py
└── reports/                    # output, gitignored
    └── .gitkeep
```

**Principio di design:**
- `extractor` → `parser` → `diagnosis` → `prompt_builder` — pipeline lineare, ogni step testabile
- Zero dipendenze di rete, zero database, zero state persistente
- `click` è l'unica dep non-stdlib. Jinja2 opzionale (usiamo f-string se non necessario)

---

## Task 1: Bootstrap del repo

**Files:**
- Create: `README.md`
- Create: `pyproject.toml`
- Create: `.gitignore`
- Create: `analyze.py`
- Create: `src/rclog/__init__.py`
- Init: repo git + remote privato `LuDattilo/revitcortex-log-analyzer`

- [ ] **Step 1: Crea cartella e git init**

```bash
mkdir "C:\Users\luigi.dattilo\Desktop\ClaudeCode\revitcortex-log-analyzer"
cd "C:\Users\luigi.dattilo\Desktop\ClaudeCode\revitcortex-log-analyzer"
git init
```

- [ ] **Step 2: Scrivi README.md**

```markdown
# RevitCortex Log Analyzer

CLI che converte i bug report degli utenti RevitCortex (ZIP prodotti dal bottone "Send log to support") in prompt markdown per Claude Code.

## Uso

```bash
pip install -e .
rclog analyze path/to/RevitCortex-BugReport-user-20260416-224745.zip
# → genera reports/<bug-id>/prompt.md
```

Copia il contenuto di `prompt.md` in una nuova sessione Claude Code aperta nel repo `RevitCortex`.

## Stack

Python 3.11+, stdlib only + `click` per CLI. Zero rete, zero DB, zero state.
```

- [ ] **Step 3: Scrivi pyproject.toml**

```toml
[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[project]
name = "rclog"
version = "0.1.0"
description = "RevitCortex bug report log analyzer"
readme = "README.md"
requires-python = ">=3.11"
dependencies = [
    "click>=8.1",
]

[project.scripts]
rclog = "rclog.cli:cli"

[tool.hatch.build.targets.wheel]
packages = ["src/rclog"]
```

- [ ] **Step 4: Scrivi .gitignore**

```
__pycache__/
*.pyc
.venv/
venv/
dist/
*.egg-info/
reports/
!reports/.gitkeep
.pytest_cache/
.coverage
```

- [ ] **Step 5: Crea cartelle skeleton + __init__.py vuoti**

```bash
mkdir -p src/rclog tests reports
type nul > src/rclog/__init__.py
type nul > reports/.gitkeep
```

- [ ] **Step 6: Crea repo GitHub privato + primo push**

```bash
gh repo create LuDattilo/revitcortex-log-analyzer --private --source=. --remote=origin --description "RevitCortex bug report analyzer - generates Claude Code prompts from tester ZIPs"
git add .
git commit -m "chore: bootstrap repo"
git push -u origin main
```

---

## Task 2: Extractor — decomprimi ZIP e trova i file

**Files:**
- Create: `src/rclog/extractor.py`
- Create: `tests/test_extractor.py`

**Contratto:** dato un path a uno ZIP, estrarre in una cartella temp e restituire un dataclass con i percorsi dei file chiave.

- [ ] **Step 1: Test fixture — copia uno ZIP di esempio**

Copia uno ZIP generato dal plugin SendSupportReport (lo trovi sul Desktop dopo aver cliccato il bottone) in `tests/fixtures/sample-bugreport.zip`. Se non ne hai uno a disposizione, genera un fake con:
```python
# tests/conftest.py (o inline nel test)
import zipfile, json, pathlib

def make_fake_zip(path):
    with zipfile.ZipFile(path, 'w') as z:
        z.writestr("audit.jsonl",
            '{"ts":"2026-04-16T20:00:00Z","tool":"ai_element_filter","input_summary":"(no params)","result":"ok","elements_affected":0}\n'
            '{"ts":"2026-04-16T20:00:05Z","tool":"send_code_to_revit","input_summary":"code(100 chars)","result":"fail","error_code":"PermissionDenied","elements_affected":0}\n')
        z.writestr("logs/token-usage.jsonl",
            '{"timestamp":"2026-04-16T20:00:00Z","toolName":"ai_element_filter","durationMs":7500,"responseChars":12000,"estimatedTokens":3000,"isError":false}\n')
        z.writestr("settings.json", '{"Port": 8080, "EnableCodeExecution": false}')
        z.writestr("context.txt",
            "RevitCortex diagnostic context\n"
            "Generated: 2026-04-16 20:00:00\n"
            "User:      john.doe\n"
            "Machine:   DESKTOP-ABC\n"
            "Revit:     Autodesk Revit 2025 build 20240904_1515 (25.0.0.0)\n"
            "Document:  Project-XYZ\n")
```

- [ ] **Step 2: Scrivi il test (fail prima dell'implementazione)**

`tests/test_extractor.py`:
```python
import zipfile, tempfile, pathlib, pytest
from rclog.extractor import extract_bugreport, BugReport

def _make_zip(dest):
    with zipfile.ZipFile(dest, 'w') as z:
        z.writestr("audit.jsonl", '{"ts":"2026-04-16T20:00:00Z","tool":"x","result":"ok","elements_affected":0}\n')
        z.writestr("logs/token-usage.jsonl", '{"timestamp":"2026-04-16T20:00:00Z","toolName":"x","durationMs":10,"isError":false}\n')
        z.writestr("settings.json", '{"Port":8080}')
        z.writestr("context.txt", "User: alice\nRevit: Revit 2025\n")

def test_extract_finds_all_files(tmp_path):
    zip_path = tmp_path / "sample.zip"
    _make_zip(zip_path)
    report = extract_bugreport(zip_path, tmp_path / "out")
    assert isinstance(report, BugReport)
    assert report.audit_path.name == "audit.jsonl"
    assert report.token_usage_path.name == "token-usage.jsonl"
    assert report.settings_path.name == "settings.json"
    assert report.context_path.name == "context.txt"

def test_extract_missing_optional_files_ok(tmp_path):
    zip_path = tmp_path / "sample.zip"
    with zipfile.ZipFile(zip_path, 'w') as z:
        z.writestr("audit.jsonl", "")  # audit is required
    report = extract_bugreport(zip_path, tmp_path / "out")
    assert report.token_usage_path is None
    assert report.settings_path is None

def test_extract_missing_audit_raises(tmp_path):
    zip_path = tmp_path / "sample.zip"
    with zipfile.ZipFile(zip_path, 'w') as z:
        z.writestr("other.txt", "nothing")
    with pytest.raises(ValueError, match="audit.jsonl"):
        extract_bugreport(zip_path, tmp_path / "out")
```

- [ ] **Step 3: Run — deve fallire (modulo non esiste)**

```bash
pip install -e .
pip install pytest
pytest tests/test_extractor.py -v
```
Expected: `ModuleNotFoundError: rclog.extractor`.

- [ ] **Step 4: Implementa extractor.py**

```python
"""Extract RevitCortex bug report ZIPs and locate the key files."""
from __future__ import annotations
import zipfile
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class BugReport:
    """Filesystem paths to the files found inside a bug-report ZIP."""
    zip_name: str
    extract_dir: Path
    audit_path: Path                  # required
    token_usage_path: Path | None     # optional
    settings_path: Path | None        # optional
    context_path: Path | None         # optional
    journal_path: Path | None         # optional


def extract_bugreport(zip_path: Path, out_dir: Path) -> BugReport:
    """Extract a RevitCortex-BugReport-*.zip and return resolved paths.

    Raises ValueError if audit.jsonl is missing (the only required artifact).
    """
    zip_path = Path(zip_path)
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(zip_path, 'r') as z:
        z.extractall(out_dir)

    def _find(name: str) -> Path | None:
        # recursive search; entries may be at root or nested (e.g. logs/token-usage.jsonl)
        for candidate in out_dir.rglob(name):
            if candidate.is_file():
                return candidate
        return None

    audit = _find("audit.jsonl")
    if audit is None:
        raise ValueError(f"{zip_path.name}: audit.jsonl not found (required)")

    # journal file has a dynamic name, find first .txt under journal/
    journal = None
    journal_dir = out_dir / "journal"
    if journal_dir.exists():
        for f in journal_dir.glob("*.txt"):
            journal = f
            break

    return BugReport(
        zip_name=zip_path.name,
        extract_dir=out_dir,
        audit_path=audit,
        token_usage_path=_find("token-usage.jsonl"),
        settings_path=_find("settings.json"),
        context_path=_find("context.txt"),
        journal_path=journal,
    )
```

- [ ] **Step 5: Run test — deve passare**

```bash
pytest tests/test_extractor.py -v
```
Expected: 3 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/rclog/extractor.py tests/test_extractor.py tests/fixtures/sample-bugreport.zip
git commit -m "feat(extractor): extract bug-report ZIPs and resolve key file paths"
```

---

## Task 3: Parser — carica audit.jsonl e token-usage.jsonl in dataclass

**Files:**
- Create: `src/rclog/parser.py`
- Create: `tests/test_parser.py`

**Contratto:** stream JSONL → lista di dataclass tipizzati.

- [ ] **Step 1: Test**

`tests/test_parser.py`:
```python
import pytest
from pathlib import Path
from rclog.parser import (
    parse_audit, parse_token_usage,
    AuditEntry, TokenUsageEntry,
)

def test_parse_audit_reads_all_lines(tmp_path):
    p = tmp_path / "audit.jsonl"
    p.write_text(
        '{"ts":"2026-04-16T20:00:00Z","tool":"a","input_summary":"x","result":"ok","elements_affected":0}\n'
        '{"ts":"2026-04-16T20:00:05Z","tool":"b","input_summary":"y","result":"fail","error_code":"InvalidInput","elements_affected":3}\n'
    )
    entries = parse_audit(p)
    assert len(entries) == 2
    assert entries[0].tool == "a"
    assert entries[0].success is True
    assert entries[0].error_code is None
    assert entries[1].success is False
    assert entries[1].error_code == "InvalidInput"
    assert entries[1].elements_affected == 3

def test_parse_audit_skips_malformed_lines(tmp_path):
    p = tmp_path / "audit.jsonl"
    p.write_text(
        '{"ts":"2026-04-16T20:00:00Z","tool":"a","result":"ok","elements_affected":0}\n'
        'not json at all\n'
        '{"ts":"2026-04-16T20:00:05Z","tool":"b","result":"ok","elements_affected":0}\n'
    )
    entries = parse_audit(p)
    assert len(entries) == 2  # malformed line skipped

def test_parse_token_usage(tmp_path):
    p = tmp_path / "tu.jsonl"
    p.write_text(
        '{"timestamp":"2026-04-16T20:00:00Z","toolName":"x","durationMs":77,"responseChars":100,"estimatedTokens":25,"isError":false}\n'
    )
    entries = parse_token_usage(p)
    assert len(entries) == 1
    assert entries[0].tool == "x"
    assert entries[0].duration_ms == 77
    assert entries[0].is_error is False
```

- [ ] **Step 2: Implementa parser.py**

```python
"""Parse RevitCortex JSONL log streams into typed records."""
from __future__ import annotations
import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


@dataclass(frozen=True)
class AuditEntry:
    ts: datetime
    tool: str
    input_summary: str
    success: bool
    error_code: str | None
    elements_affected: int


@dataclass(frozen=True)
class TokenUsageEntry:
    ts: datetime
    tool: str
    duration_ms: int
    response_chars: int
    estimated_tokens: int
    is_error: bool


def _parse_ts(s: str) -> datetime:
    # Revit writes timestamps like "2026-04-16T20:00:00.1234567Z" — datetime.fromisoformat
    # since 3.11 accepts Z directly.
    try:
        return datetime.fromisoformat(s.replace("Z", "+00:00"))
    except ValueError:
        return datetime.min


def parse_audit(path: Path) -> list[AuditEntry]:
    entries = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            d = json.loads(line)
        except json.JSONDecodeError:
            continue
        entries.append(AuditEntry(
            ts=_parse_ts(d.get("ts", "")),
            tool=d.get("tool", "?"),
            input_summary=d.get("input_summary", ""),
            success=d.get("result") == "ok",
            error_code=d.get("error_code"),
            elements_affected=int(d.get("elements_affected", 0)),
        ))
    return entries


def parse_token_usage(path: Path) -> list[TokenUsageEntry]:
    entries = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            d = json.loads(line)
        except json.JSONDecodeError:
            continue
        entries.append(TokenUsageEntry(
            ts=_parse_ts(d.get("timestamp", "")),
            tool=d.get("toolName", "?"),
            duration_ms=int(d.get("durationMs", 0)),
            response_chars=int(d.get("responseChars", 0)),
            estimated_tokens=int(d.get("estimatedTokens", 0)),
            is_error=bool(d.get("isError", False)),
        ))
    return entries
```

- [ ] **Step 3: Run test**

```bash
pytest tests/test_parser.py -v
```
Expected: 3 PASS.

- [ ] **Step 4: Commit**

```bash
git add src/rclog/parser.py tests/test_parser.py
git commit -m "feat(parser): load audit.jsonl and token-usage.jsonl into typed records"
```

---

## Task 4: Diagnosis — euristiche per interpretare il log

**Files:**
- Create: `src/rclog/diagnosis.py`
- Create: `tests/test_diagnosis.py`

**Contratto:** dato un `BugReport` parsato, produrre una `Diagnosis` con:
- **Error clusters**: errori raggruppati per `(tool, error_code)` con count e ultimo ts
- **Slow tools**: top 5 per durationMs max
- **Failed tools**: tool che hanno almeno un fail
- **Suspect files**: path nel repo RevitCortex probabilmente correlati (basato su nome tool → classe C# convention)
- **Hypotheses**: frasi testuali tipo "send_code_to_revit fallisce con PermissionDenied 5 volte — probabile EnableCodeExecution=false"

**Principio chiave:** la diagnosi è **euristica**, non deve essere corretta al 100%. Serve a dare a Claude un punto di partenza denso.

- [ ] **Step 1: Test**

`tests/test_diagnosis.py`:
```python
from datetime import datetime
from rclog.parser import AuditEntry, TokenUsageEntry
from rclog.diagnosis import diagnose, Diagnosis


def _mk_audit(tool, success=True, error_code=None):
    return AuditEntry(
        ts=datetime(2026, 4, 16, 20, 0), tool=tool, input_summary="",
        success=success, error_code=error_code, elements_affected=0,
    )


def test_diagnose_groups_errors():
    entries = [
        _mk_audit("send_code_to_revit", False, "PermissionDenied"),
        _mk_audit("send_code_to_revit", False, "PermissionDenied"),
        _mk_audit("ai_element_filter", True),
    ]
    d = diagnose(entries, [], None)
    assert any(c.tool == "send_code_to_revit" and c.count == 2 for c in d.error_clusters)


def test_diagnose_no_errors_empty_clusters():
    d = diagnose([_mk_audit("x")], [], None)
    assert d.error_clusters == []


def test_diagnose_slow_tools_ordered_desc():
    tu = [
        TokenUsageEntry(datetime(2026, 4, 16), "fast",     100, 0, 0, False),
        TokenUsageEntry(datetime(2026, 4, 16), "medium", 2_000, 0, 0, False),
        TokenUsageEntry(datetime(2026, 4, 16), "slow",  10_000, 0, 0, False),
    ]
    d = diagnose([], tu, None)
    assert d.slow_tools[0].tool == "slow"
    assert d.slow_tools[0].duration_ms == 10_000


def test_suspect_files_for_known_tool():
    entries = [_mk_audit("ai_element_filter", False, "Unknown")]
    d = diagnose(entries, [], None)
    # Expect path hints toward the tool's C# class
    assert any("AIElementFilterTool" in p for p in d.suspect_files)
```

- [ ] **Step 2: Implementa diagnosis.py**

```python
"""Heuristics that turn parsed log entries into an actionable diagnosis."""
from __future__ import annotations
import re
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from .parser import AuditEntry, TokenUsageEntry


@dataclass(frozen=True)
class ErrorCluster:
    tool: str
    error_code: str | None
    count: int
    last_seen: datetime
    sample_summary: str  # last input_summary we saw for this cluster


@dataclass(frozen=True)
class SlowTool:
    tool: str
    duration_ms: int
    ts: datetime


@dataclass(frozen=True)
class Diagnosis:
    total_calls: int
    failed_calls: int
    error_clusters: list[ErrorCluster]
    slow_tools: list[SlowTool]
    suspect_files: list[str]      # repo-relative paths, guesses
    hypotheses: list[str]         # plain-English lines for the prompt


# Pattern: tool name in snake_case maps to a Pascal-case C# class in the Tools project.
# Not always exact, but a good starting hint.
def _tool_to_class(tool: str) -> str:
    parts = tool.split("_")
    return "".join(p.capitalize() for p in parts) + "Tool"


# Known categories to guess the folder inside src/RevitCortex.Tools/
_CATEGORY_HINTS = {
    "ai_element_filter": "Elements",
    "get_element_parameters": "Elements",
    "set_element_parameters": "Elements",
    "send_code_to_revit": "Elements",
    "export_to_excel": "Elements",
    "color_elements": "Elements",
    "create_": "Elements",
    "workflow_": "Workflows",
    "ifc_": "Ifc",
    "tag_": "Annotations",
    "wipe_empty_tags": "Annotations",
    "create_dimensions": "Annotations",
    "create_text_note": "Annotations",
    "analyze_": "Project",
    "manage_": "Project",
    "check_model_health": "Project",
    "audit_families": "Project",
    "purge_unused": "Project",
    "get_project_info": "Project",
    "export_schedule": "Project",
    "create_schedule": "Project",
    "view_": "Views",
    "create_view": "Views",
    "duplicate_view": "Views",
    "rename_views": "Views",
    "sheet_": "Sheets",
    "create_sheet": "Sheets",
    "batch_create_sheets": "Sheets",
    "duplicate_sheet_": "Sheets",
    "bulk_modify_parameter_values": "Parameters",
    "sync_csv_parameters": "Parameters",
    "add_shared_parameter": "Parameters",
}


def _guess_category(tool: str) -> str:
    if tool in _CATEGORY_HINTS:
        return _CATEGORY_HINTS[tool]
    for prefix, cat in _CATEGORY_HINTS.items():
        if prefix.endswith("_") and tool.startswith(prefix):
            return cat
    return "Elements"  # reasonable default


def diagnose(
    audit: list[AuditEntry],
    tokens: list[TokenUsageEntry],
    context_text: str | None,
) -> Diagnosis:
    # Error clusters
    buckets: dict[tuple[str, str | None], list[AuditEntry]] = defaultdict(list)
    for e in audit:
        if e.success:
            continue
        buckets[(e.tool, e.error_code)].append(e)

    clusters = [
        ErrorCluster(
            tool=tool,
            error_code=code,
            count=len(items),
            last_seen=max(i.ts for i in items),
            sample_summary=items[-1].input_summary,
        )
        for (tool, code), items in buckets.items()
    ]
    clusters.sort(key=lambda c: c.count, reverse=True)

    # Slow tools
    slow = sorted(tokens, key=lambda t: t.duration_ms, reverse=True)[:5]
    slow = [SlowTool(tool=t.tool, duration_ms=t.duration_ms, ts=t.ts) for t in slow]

    # Suspect files
    suspect: list[str] = []
    seen: set[str] = set()
    for c in clusters[:5]:
        cat = _guess_category(c.tool)
        cls = _tool_to_class(c.tool)
        path = f"src/RevitCortex.Tools/{cat}/{cls}.cs"
        if path not in seen:
            suspect.append(path)
            seen.add(path)

    # Hypotheses (plain English, will render as bullet points in the prompt)
    hypotheses: list[str] = []
    for c in clusters:
        if c.error_code == "PermissionDenied" and c.tool == "send_code_to_revit":
            hypotheses.append(
                f"`send_code_to_revit` bloccato {c.count} volte con PermissionDenied. "
                "Probabile: `EnableCodeExecution=false` in `~/.revitcortex/settings.json` "
                "(questo è il comportamento corretto post-v1.0.1; verifica se il tester "
                "doveva avere il flag abilitato)."
            )
        elif c.error_code == "InvalidInput":
            hypotheses.append(
                f"`{c.tool}` fallisce {c.count} volte con InvalidInput "
                f"(es. summary: '{c.sample_summary[:80]}'). "
                "Probabile bug di parsing o validazione parametri — verifica i parametri "
                f"richiesti in `{_tool_to_class(c.tool)}`."
            )
        elif c.error_code == "Unknown":
            hypotheses.append(
                f"`{c.tool}` fallisce {c.count} volte con errore Unknown — probabile "
                "eccezione non gestita o bug Revit-side. Controlla il journal Revit se "
                "presente + `Execute()` del tool."
            )
        else:
            hypotheses.append(
                f"`{c.tool}` fallisce {c.count} volte"
                + (f" con {c.error_code}" if c.error_code else "")
                + f" (ultimo: {c.last_seen.strftime('%Y-%m-%d %H:%M:%S')})."
            )

    # Perf hypotheses
    for s in slow[:2]:
        if s.duration_ms > 5000:
            hypotheses.append(
                f"`{s.tool}` ha impiegato {s.duration_ms} ms — oltre la soglia di 5s. "
                "Considera profiling del tool o early-exit / caching."
            )

    return Diagnosis(
        total_calls=len(audit),
        failed_calls=sum(1 for e in audit if not e.success),
        error_clusters=clusters,
        slow_tools=slow,
        suspect_files=suspect,
        hypotheses=hypotheses,
    )
```

- [ ] **Step 3: Run test**

```bash
pytest tests/test_diagnosis.py -v
```
Expected: 4 PASS.

- [ ] **Step 4: Commit**

```bash
git add src/rclog/diagnosis.py tests/test_diagnosis.py
git commit -m "feat(diagnosis): cluster errors, find slow tools, guess suspect files"
```

---

## Task 5: Prompt builder — genera il markdown per Claude Code

**Files:**
- Create: `src/rclog/prompt_builder.py`
- Create: `tests/test_prompt_builder.py`

**Contratto:** dato `BugReport` + `Diagnosis` + testi grezzi (context.txt, settings.json), produrre un singolo markdown che:

1. **Header**: identificatore del bug report (da nome ZIP) + meta
2. **Context**: un estratto del context.txt (utente, Revit, documento)
3. **Sommario quantitativo**: totale calls, fail, slow
4. **Error clusters** ordinati per priorità
5. **Suspect files** come lista ticked
6. **Hypotheses** (il cuore della diagnosi)
7. **How to investigate** (istruzioni operative per Claude)
8. **Raw excerpt** delle ultime 20 righe di audit.jsonl (il segnale grezzo, inline)

- [ ] **Step 1: Test**

`tests/test_prompt_builder.py`:
```python
from datetime import datetime
from pathlib import Path
from rclog.diagnosis import Diagnosis, ErrorCluster, SlowTool
from rclog.parser import AuditEntry
from rclog.prompt_builder import build_prompt


def test_prompt_contains_header_and_diagnosis(tmp_path):
    diag = Diagnosis(
        total_calls=10, failed_calls=3,
        error_clusters=[
            ErrorCluster("send_code_to_revit", "PermissionDenied", 3,
                         datetime(2026, 4, 16), "code(100 chars)"),
        ],
        slow_tools=[SlowTool("ai_element_filter", 8000, datetime(2026, 4, 16))],
        suspect_files=["src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs"],
        hypotheses=["send_code_to_revit bloccato 3 volte..."],
    )
    recent = [
        AuditEntry(datetime(2026, 4, 16, 20), "x", "(no params)", True, None, 0),
    ]
    md = build_prompt(
        bug_id="bugreport-20260416-224745",
        user="alice",
        revit_version="Autodesk Revit 2025",
        document="Project-X",
        diagnosis=diag,
        recent_audit=recent,
        settings_text='{"Port":8080}',
    )
    assert "bugreport-20260416-224745" in md
    assert "alice" in md
    assert "send_code_to_revit" in md
    assert "PermissionDenied" in md
    assert "SendCodeToRevitTool.cs" in md
    assert "## How to investigate" in md
```

- [ ] **Step 2: Implementa prompt_builder.py**

```python
"""Render a Diagnosis + raw excerpts into a Markdown prompt for Claude Code."""
from __future__ import annotations
from datetime import datetime

from .diagnosis import Diagnosis
from .parser import AuditEntry


_TEMPLATE = """\
# Bug report analysis — {bug_id}

> **Intent:** Claude Code should investigate this report and propose (or apply) a fix in the RevitCortex repo.

## Context

| Field | Value |
|-------|-------|
| Reporter | {user} |
| Revit | {revit_version} |
| Document | {document} |
| Total MCP calls in this session | {total_calls} |
| Failed calls | {failed_calls} |

## Error clusters

{error_clusters_md}

## Suspect source files

{suspect_files_md}

## Hypotheses

{hypotheses_md}

## Slow tools (potential perf issues)

{slow_tools_md}

## How to investigate

1. Open the suspect files above and check the `Execute` method (or equivalent entry point) of each tool.
2. Cross-check with the recent audit excerpt below: what parameters did the tool receive, what `error_code` did it return?
3. If the error is `PermissionDenied` on `send_code_to_revit`, this is likely expected (consent gate). Confirm with the reporter that `EnableCodeExecution` was supposed to be on.
4. If the error is `Unknown` or `InvalidInput` with no clear cause, look at the Revit journal (`journal/*.txt`) in the extracted ZIP for the same timestamp range.
5. Propose a minimal fix: add validation, improve error message, adjust gate semantics — whichever applies.
6. Do NOT speculate on Revit-side bugs without evidence; if unclear, ask the user for a reproducer.

## Recent audit excerpt (last 20 entries)

```jsonl
{recent_audit_md}
```

## User settings.json (context only)

```json
{settings_md}
```

---

*Generated by `rclog` from the bug-report ZIP. All file paths above are guesses — verify before editing.*
"""


def _format_cluster_row(c) -> str:
    code = f"`{c.error_code}`" if c.error_code else "—"
    return f"| `{c.tool}` | {code} | {c.count} | {c.last_seen.strftime('%Y-%m-%d %H:%M:%S')} | `{c.sample_summary[:60]}` |"


def build_prompt(
    *, bug_id: str, user: str, revit_version: str, document: str,
    diagnosis: Diagnosis, recent_audit: list[AuditEntry],
    settings_text: str | None,
) -> str:
    # Error clusters table
    if diagnosis.error_clusters:
        rows = "\n".join(_format_cluster_row(c) for c in diagnosis.error_clusters)
        clusters_md = (
            "| Tool | Error code | Count | Last seen | Sample input |\n"
            "|---|---|---|---|---|\n"
            f"{rows}"
        )
    else:
        clusters_md = "*Nessun errore registrato in questo bug report.*"

    # Suspect files
    suspect_md = (
        "\n".join(f"- [ ] [{p}]({p})" for p in diagnosis.suspect_files)
        if diagnosis.suspect_files
        else "*Nessun file specifico individuato.*"
    )

    # Hypotheses
    hypo_md = (
        "\n".join(f"- {h}" for h in diagnosis.hypotheses)
        if diagnosis.hypotheses
        else "*Nessuna ipotesi automatica generata.*"
    )

    # Slow tools
    if diagnosis.slow_tools:
        slow_rows = "\n".join(
            f"| `{s.tool}` | {s.duration_ms} ms |"
            for s in diagnosis.slow_tools
        )
        slow_md = "| Tool | Duration |\n|---|---|\n" + slow_rows
    else:
        slow_md = "*Nessuna misurazione token-usage disponibile.*"

    # Recent audit (last 20)
    recent_json_lines = []
    for e in recent_audit[-20:]:
        parts = [
            f'"ts":"{e.ts.isoformat()}"',
            f'"tool":"{e.tool}"',
            f'"result":"{"ok" if e.success else "fail"}"',
        ]
        if e.error_code:
            parts.append(f'"error_code":"{e.error_code}"')
        recent_json_lines.append("{" + ",".join(parts) + "}")
    recent_md = "\n".join(recent_json_lines) or "(empty)"

    settings_md = (settings_text or "(not provided)").strip()

    return _TEMPLATE.format(
        bug_id=bug_id,
        user=user,
        revit_version=revit_version,
        document=document,
        total_calls=diagnosis.total_calls,
        failed_calls=diagnosis.failed_calls,
        error_clusters_md=clusters_md,
        suspect_files_md=suspect_md,
        hypotheses_md=hypo_md,
        slow_tools_md=slow_md,
        recent_audit_md=recent_md,
        settings_md=settings_md,
    )
```

- [ ] **Step 3: Run test**

```bash
pytest tests/test_prompt_builder.py -v
```
Expected: 1 PASS.

- [ ] **Step 4: Commit**

```bash
git add src/rclog/prompt_builder.py tests/test_prompt_builder.py
git commit -m "feat(prompt_builder): render diagnosis into Markdown prompt for Claude Code"
```

---

## Task 6: CLI — collega tutto insieme

**Files:**
- Create: `src/rclog/cli.py`
- Create: `analyze.py` (shim)
- Create: `tests/test_cli.py`

**Contratto:** `rclog analyze path/to/bugreport.zip` esegue la pipeline e scrive `reports/<bug-id>/prompt.md` + `reports/<bug-id>/raw/*` (i file estratti).

- [ ] **Step 1: Implementa cli.py**

```python
"""rclog CLI — entry point for bug report analysis."""
from __future__ import annotations
import re
import shutil
import sys
from pathlib import Path

import click

from .extractor import extract_bugreport
from .parser import parse_audit, parse_token_usage
from .diagnosis import diagnose
from .prompt_builder import build_prompt


def _parse_context(context_text: str) -> dict:
    """Extract key=value pairs from the context.txt plaintext."""
    d = {}
    for line in context_text.splitlines():
        m = re.match(r"^\s*([A-Za-z]+):\s*(.*)$", line)
        if m:
            d[m.group(1).lower()] = m.group(2).strip()
    return d


@click.group()
def cli():
    """RevitCortex bug report analyzer."""


@cli.command()
@click.argument("zip_path", type=click.Path(exists=True, dir_okay=False, path_type=Path))
@click.option("--out", "out_root", type=click.Path(path_type=Path), default=Path("reports"),
              help="Output root folder (default: ./reports).")
def analyze(zip_path: Path, out_root: Path):
    """Analyze a single bug-report ZIP and generate a Claude Code prompt."""
    # Derive a bug-id from the zip filename (strip extension + sanitize)
    bug_id = zip_path.stem
    out_dir = out_root / bug_id
    raw_dir = out_dir / "raw"
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    click.echo(f"[rclog] Extracting {zip_path.name}...")
    report = extract_bugreport(zip_path, raw_dir)

    click.echo(f"[rclog] Parsing audit + token-usage logs...")
    audit = parse_audit(report.audit_path)
    tokens = parse_token_usage(report.token_usage_path) if report.token_usage_path else []

    context_text = report.context_path.read_text(encoding="utf-8") if report.context_path else ""
    ctx = _parse_context(context_text)

    settings_text = report.settings_path.read_text(encoding="utf-8") if report.settings_path else None

    click.echo(f"[rclog] Diagnosing... ({len(audit)} audit entries, {len(tokens)} token-usage)")
    diagnosis = diagnose(audit, tokens, context_text)

    click.echo(f"[rclog] Building prompt...")
    md = build_prompt(
        bug_id=bug_id,
        user=ctx.get("user", "(unknown)"),
        revit_version=ctx.get("revit", "(unknown)"),
        document=ctx.get("document", "(unknown)"),
        diagnosis=diagnosis,
        recent_audit=audit,
        settings_text=settings_text,
    )

    prompt_path = out_dir / "prompt.md"
    prompt_path.write_text(md, encoding="utf-8")

    # Summary to stdout
    click.echo("")
    click.echo(click.style("── Summary ──────────────────────────", fg="cyan"))
    click.echo(f"  Bug ID:         {bug_id}")
    click.echo(f"  Reporter:       {ctx.get('user', '?')}")
    click.echo(f"  Revit:          {ctx.get('revit', '?')}")
    click.echo(f"  Total calls:    {diagnosis.total_calls}")
    click.echo(f"  Failed calls:   {diagnosis.failed_calls}")
    click.echo(f"  Error clusters: {len(diagnosis.error_clusters)}")
    click.echo("")
    click.echo(f"  Prompt:         {prompt_path}")
    click.echo(f"  Raw artifacts:  {raw_dir}/")
    click.echo("")
    click.echo(click.style("Next: open the prompt.md in a new Claude Code session "
                           "inside the RevitCortex repo.", fg="green"))


if __name__ == "__main__":
    cli()
```

- [ ] **Step 2: Scrivi analyze.py shim**

```python
"""Top-level shim so you can `python analyze.py ...` without installing."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / "src"))
from rclog.cli import cli
if __name__ == "__main__":
    cli()
```

- [ ] **Step 3: Test end-to-end manuale**

Con un vero ZIP dal Desktop:
```bash
pip install -e .
rclog analyze "C:/Users/luigi.dattilo/Desktop/RevitCortex-BugReport-luigi.dattilo-20260416-224745.zip"
# → verifica che reports/RevitCortex-BugReport-.../prompt.md esista
# → aprilo e leggi: contiene sezioni, tabelle, ipotesi?
```

- [ ] **Step 4: Test CLI con click.testing**

`tests/test_cli.py`:
```python
import zipfile
from pathlib import Path
from click.testing import CliRunner
from rclog.cli import cli


def _make_zip(dest):
    with zipfile.ZipFile(dest, 'w') as z:
        z.writestr("audit.jsonl",
            '{"ts":"2026-04-16T20:00:00Z","tool":"ai_element_filter","result":"ok","elements_affected":0}\n')
        z.writestr("context.txt", "User: alice\nRevit: Revit 2025\nDocument: X\n")


def test_cli_analyze_produces_prompt(tmp_path):
    zip_path = tmp_path / "bug.zip"
    _make_zip(zip_path)
    runner = CliRunner()
    result = runner.invoke(cli, ["analyze", str(zip_path), "--out", str(tmp_path / "reports")])
    assert result.exit_code == 0, result.output
    prompt = (tmp_path / "reports" / "bug" / "prompt.md").read_text(encoding="utf-8")
    assert "# Bug report analysis" in prompt
    assert "alice" in prompt
```

- [ ] **Step 5: Run tutti i test**

```bash
pytest -v
```
Expected: tutti PASS (~10 test).

- [ ] **Step 6: Commit + push**

```bash
git add src/rclog/cli.py analyze.py tests/test_cli.py
git commit -m "feat(cli): rclog analyze CMD — end-to-end pipeline from ZIP to Claude prompt"
git push origin main
```

---

## Task 7: (Opzionale) Documentation e workflow example

- [ ] **Step 1: Aggiorna README.md con esempio completo**

Includi:
- Installazione (`pip install -e .`)
- Comando base (`rclog analyze <zip>`)
- Cosa trovi in `reports/<bug-id>/` (prompt.md + raw/)
- Come usarlo con Claude Code: "apri una nuova sessione in RevitCortex repo, incolla il contenuto di prompt.md"
- Struttura del prompt generato (sezioni, cosa significa ciascuna)

- [ ] **Step 2: Screenshot opzionale**

Genera un prompt di esempio, fai screenshot delle sezioni principali, embedda in README.

- [ ] **Step 3: Commit**

```bash
git add README.md docs/
git commit -m "docs: README with usage example and output structure"
git push
```

---

## Self-Review

- [x] **Spec coverage:** CLI on-demand, Python, repo separato, output markdown — tutto coperto
- [x] **TDD:** Task 2-4 scrivono test prima dell'impl; Task 5-6 aggiungono test
- [x] **Pipeline lineare:** extractor → parser → diagnosis → prompt_builder → CLI — ogni modulo testabile in isolamento
- [x] **Zero dipendenze pesanti:** solo stdlib + click. No DB, no rete, no cloud
- [x] **Output utile per Claude:** suspect_files sono path relativi al repo RevitCortex, hypotheses in italiano, recent audit excerpt fornisce segnale grezzo

## Note di design

1. **Perché Python e non C#**: il codebase RevitCortex è già C#, ma un tool di log-analysis beneficia di stdlib ricca di Python (json, zipfile, datetime, dataclasses). Zero dipendenze da Revit/MSBuild. Boot in 30 secondi vs 3 minuti di una solution C#.

2. **Perché repo separato**: il log analyzer NON è parte del prodotto RevitCortex (gli utenti non lo vedono, non deve essere distribuito). Vive per supportare lo sviluppo, quindi riga di separazione chiara.

3. **Perché euristiche e non ML**: 10 tester × 1-2 bug/settimana = ~100 bug/mese. Un modello ML richiederebbe migliaia di esempi etichettati. Le euristiche (regex, mapping tool→classe C#) danno il 90% del valore con 0% dell'effort.

4. **Perché markdown e non JSON**: il consumer finale è Claude Code, che legge markdown fluidamente. JSON richiederebbe istruzioni "parsate questo JSON" in ogni prompt. Markdown è il formato nativo della documentazione developer.

5. **Tempo totale stimato:** 4-6 ore di implementazione concentrata (TDD + debug). Pronto per uso reale dopo Task 6.
