import { readdirSync, statSync } from "fs";
import { join, resolve, sep } from "path";
import { homedir } from "os";

/**
 * Find Revit journal files on disk, sorted most-recent-first.
 */
export function findJournalFiles(revitVersion: string, count: number): string[] {
  const journalDir = getJournalDirectory(revitVersion);

  let entries: string[];
  try {
    entries = readdirSync(journalDir);
  } catch {
    throw new Error(
      `Journal directory not found: ${journalDir}. ` +
      `Check that Revit ${revitVersion} has been run on this machine.`
    );
  }

  // Match journal.NNNN.txt but not .abbrev or .worker*.log
  const journalFiles = entries
    .filter((f) => /^journal\.\d+\.txt$/i.test(f))
    .map((f) => {
      const fullPath = join(journalDir, f);
      const mtime = statSync(fullPath).mtimeMs;
      return { path: fullPath, mtime };
    })
    .sort((a, b) => b.mtime - a.mtime);

  if (journalFiles.length === 0) {
    throw new Error(`No journal files found in ${journalDir}`);
  }

  return journalFiles.slice(0, count).map((f) => f.path);
}

function getJournalDirectory(revitVersion: string): string {
  const localAppData =
    process.env.LOCALAPPDATA || join(homedir(), "AppData", "Local");
  return join(
    localAppData,
    "Autodesk",
    "Revit",
    `Autodesk Revit ${revitVersion}`,
    "Journals"
  );
}

/**
 * Validate a caller-supplied journal_path (H25). The path must resolve inside the
 * Revit Journals directory for the given version — otherwise an MCP caller could
 * read any file on disk (e.g. credentials, SSH keys) via readFileSync.
 * Returns the canonical absolute path, or throws on a path-traversal attempt.
 */
export function resolveJournalPathSafe(
  journalPath: string,
  revitVersion: string
): string {
  const journalDir = resolve(getJournalDirectory(revitVersion));
  const root = journalDir.endsWith(sep) ? journalDir : journalDir + sep;
  const full = resolve(journalPath);

  if (full !== journalDir && !full.startsWith(root)) {
    throw new Error(
      `journal_path must be inside the Revit ${revitVersion} Journals directory ` +
      `(${journalDir}). Refusing to read '${journalPath}'.`
    );
  }
  return full;
}
