import type { Report } from "./types";

// Bootstrap snapshots can arrive after live events. Never append the same report
// twice or replace a newer invocation count with an older snapshot.
export function mergeReports(current: Report[], incoming: Report[]): Report[] {
  const merged = new Map<string, Report>();
  for (const report of [...current, ...incoming]) {
    const id = report.id ?? `${report.time}:${report.entry}`;
    const previous = merged.get(id);
    if (!previous || (report.invocations ?? 1) >= (previous.invocations ?? 1))
      merged.set(id, report);
  }
  return [...merged.values()].slice(-100);
}
