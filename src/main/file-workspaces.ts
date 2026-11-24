import type { FileSourceGroup } from "../shared/types";
/** In-memory explicit references only. Never infer asset identity from host/user. */
export class FileWorkspaces {
  private groups = new Map<string, string[]>();
  constructor(private active: Map<string, string>) {}
  group(workspaceId: string): FileSourceGroup {
    const sourceIds = [
      ...new Set([
        workspaceId,
        ...(this.groups.get(workspaceId) ?? []),
        ...(this.active.has(workspaceId)
          ? [this.active.get(workspaceId)!]
          : []),
      ]),
    ];
    return { sourceIds, activeId: this.active.get(workspaceId) ?? workspaceId };
  }
  list() {
    return Object.fromEntries(
      [...new Set([...this.groups.keys(), ...this.active.keys()])].map((id) => [
        id,
        this.group(id),
      ]),
    );
  }
  private set(workspaceId: string, sources: string[], activeId: string) {
    this.groups.set(workspaceId, [...new Set([workspaceId, ...sources])]);
    if (activeId === workspaceId) this.active.delete(workspaceId);
    else this.active.set(workspaceId, activeId);
    return this.group(workspaceId);
  }
  replace(workspaceId: string, sourceId: string | null) {
    return this.set(
      workspaceId,
      sourceId ? [sourceId] : [],
      sourceId ?? workspaceId,
    );
  }
  attach(workspaceId: string, sourceId: string) {
    const group = this.group(workspaceId);
    if (!group.sourceIds.includes(sourceId) && group.sourceIds.length >= 32)
      throw new Error("每个工作区最多32个文件来源，请先解除部分关联");
    return this.set(workspaceId, [...group.sourceIds, sourceId], sourceId);
  }
  select(workspaceId: string, sourceId: string) {
    const group = this.group(workspaceId);
    if (!group.sourceIds.includes(sourceId))
      throw new Error("请先明确关联此文件连接");
    return this.set(workspaceId, group.sourceIds, sourceId);
  }
  detach(workspaceId: string, sourceId: string) {
    if (workspaceId === sourceId)
      throw new Error("复用此终端的文件来源不关闭，可收起文件面板");
    const group = this.group(workspaceId),
      remaining = group.sourceIds.filter((id) => id !== sourceId);
    return this.set(
      workspaceId,
      remaining,
      group.activeId === sourceId
        ? (remaining.at(-1) ?? workspaceId)
        : group.activeId,
    );
  }
  referencing(sourceId: string) {
    return Object.keys(this.list()).filter(
      (workspaceId) =>
        workspaceId !== sourceId &&
        this.group(workspaceId).sourceIds.includes(sourceId),
    );
  }
  removeWorkspace(id: string) {
    this.groups.delete(id);
    this.active.delete(id);
  }
  removeSource(id: string) {
    const changed: string[] = [];
    for (const workspaceId of Object.keys(this.list()))
      if (
        workspaceId !== id &&
        this.group(workspaceId).sourceIds.includes(id)
      ) {
        this.detach(workspaceId, id);
        changed.push(workspaceId);
      }
    return changed;
  }
}
