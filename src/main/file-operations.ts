import path from "node:path";
import { randomUUID } from "node:crypto";
import type { Sessions } from "./sessions";
import type { AppEvent, FileOperation, FilePermissions } from "../shared/types";
export interface EntryInfo {
  directory: boolean;
  link: boolean;
  mode: number | null;
  size: number;
  modified: number;
}
export function safeOperationPath(input: string): string {
  if (
    typeof input !== "string" ||
    !input.startsWith("/") ||
    input.length > 8192 ||
    input.split("/").length > 34 ||
    /[\r\n\0\\]/.test(input) ||
    input
      .split("/")
      .slice(1)
      .some((s) => s === "." || s === ".." || !s)
  )
    throw new Error("拒绝操作根目录、空路径、相对路径或不安全路径");
  const normalized = path.posix.normalize(input);
  if (normalized === "/") throw new Error("禁止修改或删除根目录");
  return normalized;
}
function childName(name: string) {
  if (!name || name === "." || name === ".." || /[\/\\\r\n\0]/.test(name))
    throw new Error("服务器返回异常目录项，已停止操作");
  return name;
}
function errorMessage(e: unknown, fallback: string) {
  const code = (e as any)?.code;
  if (code === 2) return "远程路径不存在";
  if (code === 3) return "远程权限不足";
  if (code === 8) return "服务器不支持此文件操作";
  return /[\u4e00-\u9fff]/.test(String((e as any)?.message))
    ? String((e as any).message)
    : fallback;
}
function bounded<T>(
  invoke: (done: (error: Error | null | undefined, result?: T) => void) => void,
): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(
      () => reject(new Error("文件操作超时或连接已中断")),
      15000,
    );
    try {
      invoke((e, value) => {
        clearTimeout(timer);
        e
          ? reject(new Error(errorMessage(e, "文件操作失败，请检查连接和权限")))
          : resolve(value!);
      });
    } catch {
      clearTimeout(timer);
      reject(new Error("文件连接已中断"));
    }
  });
}
export class FileOperations {
  private jobs = new Map<
    string,
    { operation: FileOperation; abort: AbortController }
  >();
  constructor(
    private sessions: Sessions,
    private emit: (e: AppEvent) => void,
    private hasTransfer: (id: string, path: string) => boolean,
  ) {}
  list() {
    return [...this.jobs.values()].map((j) => ({ ...j.operation }));
  }
  busy(id: string) {
    return [...this.jobs.values()].some(
      (j) => j.operation.sessionId === id && j.operation.state === "running",
    );
  }
  cancel(id: string) {
    const job = this.jobs.get(id);
    if (job?.operation.state === "running") {
      job.abort.abort();
      job.operation.message = "正在取消，已完成的删除不会回滚";
      this.publish(job.operation);
    }
  }
  close() {
    for (const j of this.jobs.values()) j.abort.abort();
  }
  private publish(operation: FileOperation) {
    this.emit({ type: "file-operation", operation: { ...operation } });
  }
  private async stat(id: string, file: string): Promise<EntryInfo> {
    const ftp = this.sessions.ftp(id);
    if (ftp) return ftp.inspect(file);
    const s = await this.sessions.sftp(id);
    const attrs = await this.sessions.fileCall<any>(id, "读取文件属性", (cb) =>
      s.lstat(file, (e, a) => cb(e, a)),
    );
    return {
      directory: attrs.isDirectory(),
      link: attrs.isSymbolicLink(),
      mode: attrs.mode & 0o7777,
      size: attrs.size,
      modified: attrs.mtime,
    };
  }
  private async names(id: string, file: string): Promise<string[]> {
    const ftp = this.sessions.ftp(id);
    if (ftp) return (await ftp.list(file)).files.map((f) => f.name);
    const s = await this.sessions.sftp(id);
    const entries = await this.sessions.fileCall<any[]>(
      id,
      "读取目录项",
      (cb) => s.readdir(file, (e, a) => cb(e, a)),
    );
    return entries
      .map((f) => f.filename)
      .filter((n) => n !== "." && n !== "..");
  }
  private async parents(id: string, file: string, check?: () => void) {
    const parts = file.split("/").filter(Boolean);
    const deadline = Date.now() + 10000;
    for (let i = 1; i < parts.length; i++) {
      check?.();
      if (Date.now() > deadline) throw new Error("父路径检查超时，未继续操作");
      const parent = await this.stat(id, "/" + parts.slice(0, i).join("/"));
      if (parent.link || !parent.directory)
        throw new Error("不通过符号链接或非目录父路径执行删除/权限操作");
    }
  }
  async permissions(id: string, input: string): Promise<FilePermissions> {
    const file = safeOperationPath(input);
    await this.parents(id, file);
    const info = await this.stat(id, file),
      ftp = this.sessions.ftp(id);
    return {
      sessionId: id,
      path: file,
      mode: info.mode,
      supported:
        !info.link && info.mode !== null && (!ftp || ftp.chmodSupported),
      link: info.link,
      message: info.link
        ? "符号链接不修改外部目标"
        : info.mode === null
          ? "服务器未提供可验证的权限，不能显示为 000"
          : ftp && !ftp.chmodSupported
            ? "FTP 服务器未确认 SITE CHMOD 能力"
            : undefined,
    };
  }
  async chmod(
    id: string,
    input: string,
    normal: number,
    expected: number,
  ): Promise<FilePermissions> {
    const file = safeOperationPath(input);
    if (this.busy(id) || this.hasTransfer(id, file))
      throw new Error("文件操作或相关传输进行中，请先结束再修改权限");
    if (!Number.isInteger(normal) || normal < 0 || normal > 0o777)
      throw new Error("普通权限须为 000–777，特殊位单独保留");
    const before = await this.permissions(id, file);
    if (!before.supported || before.mode === null)
      throw new Error(before.message ?? "当前服务器不支持权限修改");
    if (before.mode !== expected)
      throw new Error("权限在打开对话框后已改变，请重新读取");
    const target = (before.mode & 0o7000) | normal;
    const ftp = this.sessions.ftp(id);
    if (ftp) await ftp.chmod(file, target);
    else {
      const channel = await this.sessions.sftp(id);
      await this.sessions.fileCall<void>(id, "修改权限", (cb) =>
        channel.chmod(file, target, (e) => cb(e)),
      );
    }
    const after = await this.permissions(id, file);
    if (after.mode !== target)
      throw new Error("服务器未按请求应用权限，请重新读取核对");
    return after;
  }
  async remove(id: string, input: string): Promise<void> {
    const file = safeOperationPath(input);
    if (this.busy(id)) throw new Error("此文件连接已有目录操作，请等待或取消");
    if (this.hasTransfer(id, file))
      throw new Error("目标路径涉及运行中的传输，请先完成或取消传输");
    for (const [key, j] of this.jobs)
      if (this.jobs.size >= 100 && j.operation.state !== "running")
        this.jobs.delete(key);
    const operation: FileOperation = {
      id: randomUUID(),
      sessionId: id,
      path: file,
      state: "running",
      count: 0,
      message: "正在检查目录边界与内容",
    };
    const abort = new AbortController();
    this.jobs.set(operation.id, { operation, abort });
    this.publish(operation);
    const deadline = Date.now() + 120000,
      plan: { path: string; info: EntryInfo }[] = [],
      seen = new Set<string>();
    const check = () => {
      if (abort.signal.aborted) throw new Error("操作已取消");
      if (Date.now() > deadline)
        throw new Error("目录操作超过两分钟上限，已停止");
      if (plan.length > 2000 || seen.size > 2000)
        throw new Error("目录超过 2000 项上限，未继续删除");
    };
    let timedOut = false;
    const cancel = () => this.sessions.resetFileChannel(id);
    abort.signal.addEventListener("abort", cancel, { once: true });
    const budget = setTimeout(() => {
      timedOut = true;
      abort.abort();
    }, 120000);
    try {
      await this.parents(id, file, check);
      const walk = async (target: string, depth: number) => {
        check();
        if (depth > 32) throw new Error("目录超过 32 层深度上限");
        if (seen.has(target))
          throw new Error("服务器返回重复目录项，未继续操作");
        seen.add(target);
        const info = await this.stat(id, target);
        if (info.link && this.sessions.ftp(id))
          throw new Error("FTP 符号链接语义无法确认，拒绝递归删除；未跟随目标");
        if (info.directory && !info.link) {
          for (const name of await this.names(id, target)) {
            check();
            const child = path.posix.join(target, childName(name));
            if (!child.startsWith(target + "/"))
              throw new Error("目录项越界，已停止");
            await walk(child, depth + 1);
          }
        }
        plan.push({ path: target, info });
      };
      await walk(file, 0);
      operation.message = `已检查 ${plan.length} 项，开始逐项删除`;
      this.publish(operation);
      for (const item of plan) {
        check();
        await this.parents(id, item.path, check);
        const current = await this.stat(id, item.path);
        if (
          current.link !== item.info.link ||
          current.directory !== item.info.directory
        )
          throw new Error("删除过程中目录项类型发生变化，已停止");
        const ftp = this.sessions.ftp(id);
        if (ftp)
          await ftp.action(
            current.directory && !current.link ? "rmdir" : "remove",
            item.path,
          );
        else {
          const s = await this.sessions.sftp(id);
          try {
            await this.sessions.fileCall<void>(id, "删除目录项", (cb) =>
              current.directory && !current.link
                ? s.rmdir(item.path, (e) => cb(e))
                : s.unlink(item.path, (e) => cb(e)),
            );
          } catch (e) {
            if (current.directory && !current.link) {
              try {
                if ((await this.names(id, item.path)).length)
                  throw new Error("目录仍非空，可能有新文件写入，已停止");
              } catch (other) {
                if (/仍非空/.test(String(other))) throw other;
              }
            }
            throw e;
          }
        }
        operation.count++;
        operation.message = `已删除 ${operation.count} / ${plan.length} 项`;
        this.publish(operation);
      }
      operation.state = "done";
      operation.message = `已删除 ${operation.count} 项（包含所选项）`;
    } catch (e) {
      operation.state =
        abort.signal.aborted && !timedOut ? "cancelled" : "error";
      operation.message = `${timedOut ? "目录操作超时" : abort.signal.aborted ? "操作已取消" : errorMessage(e, "连接中断或文件操作失败")}；已确认删除 ${operation.count} 项，中断请求结果可能未知，已完成部分无法自动回滚`;
      throw new Error(operation.message);
    } finally {
      clearTimeout(budget);
      abort.signal.removeEventListener("abort", cancel);
      this.publish(operation);
    }
  }
}
