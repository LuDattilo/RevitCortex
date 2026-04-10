/**
 * Response compactor — reduces token usage by stripping empty values,
 * truncating large arrays, and providing compact summaries.
 */

/** Strip null, undefined, empty string, and empty array values recursively. */
function stripEmpty(obj: unknown): unknown {
  if (obj === null || obj === undefined) return undefined;
  if (Array.isArray(obj)) {
    return obj.map((item) => stripEmpty(item)).filter((item) => item !== undefined);
  }
  if (typeof obj === "object") {
    const result: Record<string, unknown> = {};
    for (const key of Object.keys(obj as Record<string, unknown>)) {
      const value = (obj as Record<string, unknown>)[key];
      if (value === null || value === undefined || value === "") continue;
      if (Array.isArray(value) && value.length === 0) continue;
      const cleaned = stripEmpty(value);
      if (cleaned !== undefined) {
        result[key] = cleaned;
      }
    }
    return Object.keys(result).length > 0 ? result : undefined;
  }
  return obj;
}

function isPrimitiveArray(arr: unknown[]): boolean {
  return arr.every(
    (item) => typeof item === "string" || typeof item === "number" || typeof item === "boolean"
  );
}

/**
 * Truncate arrays to maxItems, adding _truncated and _totalCount metadata.
 * Skips small primitive arrays (< 20 items).
 */
function truncateArrays(obj: unknown, maxItems: number = 100): unknown {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) {
    const processed = obj.map((item) => truncateArrays(item, maxItems));
    if (isPrimitiveArray(processed) && processed.length < 20) return processed;
    if (processed.length > maxItems) return processed.slice(0, maxItems);
    return processed;
  }
  if (typeof obj === "object") {
    const result: Record<string, unknown> = {};
    for (const key of Object.keys(obj as Record<string, unknown>)) {
      const value = (obj as Record<string, unknown>)[key];
      if (Array.isArray(value)) {
        const processed = value.map((item) => truncateArrays(item, maxItems));
        if (isPrimitiveArray(processed) && processed.length < 20) {
          result[key] = processed;
          continue;
        }
        if (processed.length > maxItems) {
          result[key] = processed.slice(0, maxItems);
          result["_truncated"] = true;
          result[`_totalCount_${key}`] = value.length;
        } else {
          result[key] = processed;
        }
      } else {
        result[key] = truncateArrays(value, maxItems);
      }
    }
    return result;
  }
  return obj;
}

/**
 * Compact mode: replace large data arrays with summary counts.
 * Keeps primitive arrays and small arrays (< 5 items) intact.
 */
function compactSummary(obj: unknown): unknown {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) return obj.map((item) => compactSummary(item));
  if (typeof obj === "object") {
    const result: Record<string, unknown> = {};
    for (const key of Object.keys(obj as Record<string, unknown>)) {
      const value = (obj as Record<string, unknown>)[key];
      if (Array.isArray(value)) {
        if (value.length < 5 || isPrimitiveArray(value)) {
          result[key] = value.map((item) => compactSummary(item));
        } else {
          result[key] = `${value.length} items`;
          result[`${key}Count`] = value.length;
        }
      } else {
        result[key] = compactSummary(value);
      }
    }
    return result;
  }
  return obj;
}

/** Apply all optimizations based on options. */
export function compactResponse(
  response: unknown,
  options?: { compact?: boolean; maxArrayItems?: number; stripNulls?: boolean }
): unknown {
  const opts = {
    compact: options?.compact ?? false,
    maxArrayItems: options?.maxArrayItems ?? 100,
    stripNulls: options?.stripNulls ?? true,
  };

  let result = response;
  if (opts.stripNulls) result = stripEmpty(result);
  if (opts.compact) {
    result = compactSummary(result);
  } else {
    result = truncateArrays(result, opts.maxArrayItems);
  }
  return result;
}
