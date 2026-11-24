import type { FileSourceGroup, SessionInfo } from "../shared/types";
type Edge = readonly [string, string];
function edges(
  associations: Record<string, string>,
  groups: Record<string, FileSourceGroup>,
): Edge[] {
  return [
    ...Object.entries(associations).filter(([owner]) => !groups[owner]),
    ...Object.entries(groups).flatMap(([owner, group]) =>
      group.sourceIds.map((source) => [owner, source] as const),
    ),
  ].filter(([owner, source]) => owner !== source);
}
function owners(id: string, links: Edge[]) {
  return links.filter(([, source]) => source === id).map(([owner]) => owner);
}
export function workspaceForTab(
  id: string,
  visible: readonly SessionInfo[],
  associations: Record<string, string>,
  groups: Record<string, FileSourceGroup> = {},
) {
  const tabs = new Set(visible.map((s) => s.id)),
    seen = new Set<string>(),
    queue = [id],
    links = edges(associations, groups);
  for (let i = 0; i < queue.length; i++) {
    const candidate = queue[i];
    if (seen.has(candidate)) continue;
    seen.add(candidate);
    if (tabs.has(candidate)) return candidate;
    queue.push(...owners(candidate, links));
  }
  return visible.at(-1)?.id ?? "";
}
/** All explicitly associated file tabs hide, including inactive sources, without closing them. */
export function visibleWorkspaceTabs(
  sessions: readonly SessionInfo[],
  associations: Record<string, string>,
  groups: Record<string, FileSourceGroup> = {},
) {
  const ids = new Set(sessions.map((s) => s.id)),
    links = edges(associations, groups);
  const hidden = new Set(
    links
      .filter(([owner, source]) => ids.has(owner) && ids.has(source))
      .map(([, source]) => source),
  );
  for (const s of sessions)
    if (s.protocol !== "sftp" && s.protocol !== "ftp") hidden.delete(s.id);
  for (const s of sessions) {
    if (!hidden.has(s.id)) continue;
    const seen = new Set<string>(),
      queue = owners(s.id, links);
    let reachable = false;
    for (let i = 0; i < queue.length; i++) {
      const owner = queue[i];
      if (seen.has(owner) || !ids.has(owner)) continue;
      seen.add(owner);
      if (!hidden.has(owner)) {
        reachable = true;
        break;
      }
      queue.push(...owners(owner, links));
    }
    if (!reachable) hidden.delete(s.id);
  }
  return sessions.filter((s) => !hidden.has(s.id));
}
