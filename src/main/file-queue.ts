export class FileRequestError extends Error {}
interface Task {
  run: () => Promise<unknown>;
  resolve: (value: any) => void;
  reject: (error: unknown) => void;
  signal?: AbortSignal;
  abort?: () => void;
  kind: "metadata" | "transfer";
}
export class FileQueue {
  private lanes = new Map<string, { active: boolean; tasks: Task[] }>();
  busy(id: string) {
    const lane = this.lanes.get(id);
    return !!lane && (lane.active || lane.tasks.length > 0);
  }
  run<T>(
    id: string,
    run: () => Promise<T>,
    signal?: AbortSignal,
    kind: "metadata" | "transfer" = "metadata",
  ): Promise<T> {
    if (signal?.aborted) return Promise.reject(new Error("文件请求已取消"));
    let lane = this.lanes.get(id);
    if (!lane) {
      lane = { active: false, tasks: [] };
      this.lanes.set(id, lane);
    }
    const current = lane;
    const promise = new Promise<T>((resolve, reject) => {
      const task: Task = { run, resolve, reject, signal, kind };
      task.abort = () => {
        const index = current.tasks.indexOf(task);
        if (index >= 0) {
          current.tasks.splice(index, 1);
          reject(new Error("文件请求已取消"));
        }
      };
      signal?.addEventListener("abort", task.abort, { once: true });
      if (kind === "metadata") {
        const next = current.tasks.findIndex((t) => t.kind === "transfer");
        current.tasks.splice(next < 0 ? current.tasks.length : next, 0, task);
      } else current.tasks.push(task);
    });
    void this.pump(id, current);
    return promise;
  }
  private async pump(id: string, lane: { active: boolean; tasks: Task[] }) {
    if (lane.active) return;
    lane.active = true;
    try {
      while (lane.tasks.length) {
        const task = lane.tasks.shift()!;
        task.signal?.removeEventListener("abort", task.abort!);
        if (task.signal?.aborted) {
          task.reject(new Error("文件请求已取消"));
          continue;
        }
        try {
          task.resolve(await task.run());
        } catch (e) {
          task.reject(e);
        }
      }
    } finally {
      lane.active = false;
      if (!lane.tasks.length) this.lanes.delete(id);
    }
  }
}
