import { readdirSync, statSync } from "fs";
import { join } from "path";
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
