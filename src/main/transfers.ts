import { FileRequestError } from "./file-queue";
import { createReadStream, createWriteStream } from "node:fs";
import { stat, rename, rm } from "node:fs/promises";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { Transform } from "node:stream";
import { pipeline } from "node:stream/promises";
import type { AppEvent, Transfer } from "../shared/types";
import type { Sessions } from "./sessions";
interface Job {
  item: Transfer;
  abort: AbortController;
  remote: string;
  reason?: "user" | "stalled" | "prior" | "close";
}
export class Transfers {
  private jobs = new Map<string, Job>();
  constructor(
    private sessions: Sessions,
    private emit: (e: AppEvent) => void,
    private stallMs = 30000,
  ) {}
  list() {
    return [...this.jobs.values()].map((j) => ({ ...j.item }));
  }
  activeInPath(id: string, file: string) {
    const target = path.posix.normalize(file);
    return [...this.jobs.values()].some(
      (j) =>
        j.item.sessionId === id &&
        ["queued", "running"].includes(j.item.state) &&
        (path.posix.normalize(j.remote) === target ||
          path.posix.normalize(j.remote).startsWith(target + "/") ||
          target.startsWith(path.posix.normalize(j.remote) + "/")),
    );
  }
  clearCompleted() {
    for (const [id, j] of this.jobs)
      if (!["queued", "running"].includes(j.item.state)) this.jobs.delete(id);
    this.emit({ type: "transfers-cleared" });
  }
  private update(j: Job) {
    this.emit({ type: "transfer", transfer: { ...j.item } });
  }
  cancel(id: string) {
    const j = this.jobs.get(id);
    if (!j || !["queued", "running"].includes(j.item.state)) return;
    j.reason = "user";
    j.abort.abort();
    if (j.item.state === "queued") {
      j.item.state = "cancelled";
      j.item.message = "已取消排队，未打开文件流";
      this.update(j);
    }
  }
  cancelSession(id: string) {
    for (const j of this.jobs.values())
      if (j.item.sessionId === id) {
        j.reason = "close";
        j.abort.abort();
        if (j.item.state === "queued") {
          j.item.state = "cancelled";
          j.item.message = "文件连接已关闭，未开始";
          this.update(j);
        }
      }
  }
  close() {
    for (const j of this.jobs.values()) {
      j.reason = "close";
      j.abort.abort();
    }
  }
  private stopPending(active: Job) {
    for (const j of this.jobs.values())
      if (
        j !== active &&
        j.item.sessionId === active.item.sessionId &&
        j.item.state === "queued"
      ) {
        j.reason = "prior";
        j.item.state = "error";
        j.item.message =
          "前一项失败或取消，此项未开始；请检查后手动重新选择，不会自动重传";
        j.abort.abort();
        this.update(j);
      }
  }
  private wait<T>(
    promise: Promise<T>,
    signal: AbortSignal,
    timeout = 0,
  ): Promise<T> {
    return new Promise((resolve, reject) => {
      let settled = false;
      let timer: NodeJS.Timeout | undefined;
      const finish = (error?: unknown, value?: T) => {
        if (settled) return;
        settled = true;
        if (timer) clearTimeout(timer);
        signal.removeEventListener("abort", cancel);
        error ? reject(error) : resolve(value!);
      };
      const cancel = () => finish(new Error("文件请求已取消"));
      signal.addEventListener("abort", cancel, { once: true });
      if (timeout)
        timer = setTimeout(
          () => finish(new Error("文件信息读取超时")),
          timeout,
        );
      promise.then(
        (value) => finish(undefined, value),
        (error) => finish(error),
      );
      if (signal.aborted) cancel();
    });
  }
  async start(
    sessionId: string,
    local: string,
    remote: string,
    direction: Transfer["direction"],
    before?: () => Promise<boolean>,
  ) {
    const job: Job = {
      item: {
        id: randomUUID(),
        sessionId,
        name: path.basename(local),
        direction,
        bytes: 0,
        total: 0,
        state: "queued",
      },
      remote,
      abort: new AbortController(),
    };
    this.jobs.set(job.item.id, job);
    this.update(job);
    void this.sessions
      .fileTask(
        sessionId,
        async () => {
          if (job.abort.signal.aborted) return;
          job.item.state = "running";
          job.item.message = "检查文件信息";
          this.update(job);
          const partial = local + ".forterm-" + job.item.id + ".part";
          let timer: NodeJS.Timeout | undefined;
          let last = Date.now();
          let streamStarted = false;
          const abort = () => {
            this.sessions.resetFileChannel(sessionId);
          };
          job.abort.signal.addEventListener("abort", abort, { once: true });
          try {
            if (before && !(await this.wait(before(), job.abort.signal))) {
              job.item.state = "cancelled";
              job.item.message = "已跳过，未打开文件流";
              return;
            }
            if (job.abort.signal.aborted) throw new Error("已取消");
            const independent = this.sessions.ftp(sessionId),
              channel = independent
                ? undefined
                : await this.wait(
                    this.sessions.sftp(sessionId),
                    job.abort.signal,
                  );
            job.item.total =
              direction === "upload"
                ? (await this.wait(stat(local), job.abort.signal, 10000)).size
                : independent
                  ? await this.wait(
                      independent.size(remote),
                      job.abort.signal,
                      10000,
                    )
                  : await this.sessions.fileCall<number>(
                      sessionId,
                      "读取文件信息",
                      (cb) => channel!.stat(remote, (e, a) => cb(e, a?.size)),
                      job.abort.signal,
                    );
            if (job.abort.signal.aborted) throw new Error("已取消");
            job.item.message = undefined;
            this.update(job);
            streamStarted = true;
            last = Date.now();
            timer = setInterval(
              () => {
                if (Date.now() - last > this.stallMs) {
                  job.reason = "stalled";
                  job.abort.abort();
                }
              },
              Math.max(25, Math.min(1000, this.stallMs / 4)),
            );
            timer.unref();
            let emitted = 0;
            const progress = new Transform({
              transform: (chunk, _encoding, done) => {
                job.item.bytes += chunk.length;
                last = Date.now();
                if (last - emitted > 120) {
                  emitted = last;
                  this.update(job);
                }
                done(null, chunk);
              },
            });
            let transfer: Promise<unknown>;
            if (independent) {
              const stream =
                direction === "upload"
                  ? createReadStream(local)
                  : createWriteStream(partial, { flags: "wx", mode: 0o600 });
              transfer = independent.transfer(
                direction,
                remote,
                stream,
                job.abort.signal,
                (bytes) => {
                  if (bytes > job.item.bytes) last = Date.now();
                  job.item.bytes = bytes;
                  this.update(job);
                },
              );
            } else
              transfer =
                direction === "upload"
                  ? pipeline(
                      createReadStream(local),
                      progress,
                      channel!.createWriteStream(remote),
                      { signal: job.abort.signal },
                    )
                  : pipeline(
                      channel!.createReadStream(remote),
                      progress,
                      createWriteStream(partial, { flags: "wx", mode: 0o600 }),
                      { signal: job.abort.signal },
                    );
            await new Promise<void>((resolve, reject) => {
              const cancel = () => reject(new Error("传输已中止"));
              job.abort.signal.addEventListener("abort", cancel, {
                once: true,
              });
              transfer
                .then(() => resolve(), reject)
                .finally(() =>
                  job.abort.signal.removeEventListener("abort", cancel),
                );
              if (job.abort.signal.aborted) cancel();
            });
            if (job.abort.signal.aborted) throw new Error("已取消");
            if (direction === "download") await rename(partial, local);
            job.item.state = "done";
          } catch (error) {
            job.item.state =
              job.reason === "user" || job.reason === "close"
                ? "cancelled"
                : "error";
            job.item.message =
              job.reason === "stalled"
                ? "传输停滞超时，文件通道已重置；终端保持可用"
                : job.reason === "user"
                  ? "已取消活动传输，其余排队已停止；终端保持可用"
                  : error instanceof FileRequestError
                    ? error.message
                    : (error as any)?.code === 3
                      ? "SFTP 文件权限不足，未自动重传"
                      : "传输失败，服务器拒绝文件流或文件连接中断；不会自动重复写入";
            if (direction === "upload" && streamStarted)
              job.item.message += "；远程可能保留部分文件";
            if (streamStarted) this.sessions.resetFileChannel(sessionId);
            this.stopPending(job);
            if (direction === "download")
              await rm(partial, { force: true }).catch(() => {});
          } finally {
            if (timer) clearInterval(timer);
            job.abort.signal.removeEventListener("abort", abort);
            this.update(job);
          }
        },
        job.abort.signal,
        "transfer",
      )
      .catch(() => {
        if (job.item.state === "queued") {
          job.item.state =
            job.reason === "user" || job.reason === "close"
              ? "cancelled"
              : "error";
          job.item.message = "此项未开始，文件请求已结束";
          this.update(job);
        }
      });
    return job.item.id;
  }
}
