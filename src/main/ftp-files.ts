import { Client, FTPError } from "basic-ftp";
import path from "node:path";
import type { Readable, Writable } from "node:stream";
import type { Connection, RemoteFile } from "../shared/types";
export class FtpFiles {
  private client = new Client(15000);
  private queue: Promise<unknown> = Promise.resolve();
  private root = "/";
  private ended = false;
  chmodSupported = false;
  constructor(private onClosed: () => void) {
    this.client.ftp.verbose = false;
  }
  async open(c: Connection) {
    if (
      /[\r\n\0]/.test(c.username + (c.password ?? "")) ||
      c.keyPath ||
      c.jumpId ||
      c.passphrase
    )
      throw new Error("FTP 不支持此认证/跳板配置，用户名和密码不能含控制字符");
    if (c.password === undefined)
      throw new Error("独立 FTP 文件连接需要独立用户名与密码");
    try {
      await this.client.access({
        host: c.host,
        port: c.port,
        user: c.username,
        password: c.password,
        secure: false,
      });
      this.root = await this.client.pwd();
      const help = await this.client.sendIgnoringError("HELP SITE");
      this.chmodSupported = help.code < 400 && /\bCHMOD\b/i.test(help.message);
    } catch {
      this.client.close();
      throw new Error("FTP 连接或认证失败，请检查独立文件连接信息");
    } finally {
      c.password = undefined;
    }
  }
  get closed() {
    return this.ended || this.client.closed;
  }
  close() {
    if (this.ended) return;
    this.ended = true;
    this.client.close();
    this.onClosed();
  }
  private run<T>(action: () => Promise<T>): Promise<T> {
    const result = this.queue.then(async () => {
      if (this.closed)
        throw new Error("FTP 文件连接已关闭，请明确重新建立连接");
      try {
        return await action();
      } catch (e) {
        if (this.client.closed) this.close();
        const raw = (e as any)?.message ?? "";
        if (!(e instanceof FTPError) && /[\u4e00-\u9fff]/.test(raw)) throw e;
        const code = (e as any)?.code;
        throw new Error(
          this.client.closed
            ? "FTP 连接已断开"
            : code === 500 || code === 502
              ? "FTP 服务器不支持此操作"
              : /not empty/i.test(raw)
                ? "FTP 目录非空，可能有并发写入"
                : /no such|not found|not exist/i.test(raw)
                  ? "远程路径不存在"
                  : /permission|denied|access/i.test(raw)
                    ? "FTP 权限不足"
                    : "FTP 返回文件操作失败，目标不可用或权限被拒绝",
        );
      }
    });
    this.queue = result.catch(() => {});
    return result;
  }
  private resolve(file: string) {
    if (/[\r\n\0]/.test(file)) throw new Error("FTP 路径不能含控制字符");
    return path.posix.resolve(this.root, file);
  }
  list(directory: string): Promise<{ path: string; files: RemoteFile[] }> {
    return this.run(async () => {
      const resolved = this.resolve(directory);
      const files = await this.client.list(resolved);
      return {
        path: resolved,
        files: files
          .filter((f) => ![".", ".."].includes(f.name))
          .map((f) => ({
            name: f.name,
            directory: f.isDirectory,
            size: f.size,
            modified: Math.floor((f.modifiedAt?.getTime() ?? 0) / 1000),
            mode: null,
          }))
          .sort(
            (a, b) =>
              Number(b.directory) - Number(a.directory) ||
              a.name.localeCompare(b.name),
          ),
      };
    });
  }
  size(file: string) {
    return this.run(() => this.client.size(this.resolve(file)));
  }
  exists(file: string) {
    return this.run(async () => {
      const target = this.resolve(file);
      const entries = await this.client.list(path.posix.dirname(target));
      return entries.some(
        (entry) => entry.name === path.posix.basename(target),
      );
    });
  }
  inspect(file: string): Promise<import("./file-operations").EntryInfo> {
    return this.run(async () => {
      const target = this.resolve(file),
        entries = await this.client.list(path.posix.dirname(target));
      const item = entries.find((f) => f.name === path.posix.basename(target));
      if (!item) throw new Error("远程路径不存在");
      if (!item.isDirectory && !item.isFile && !item.isSymbolicLink)
        throw new Error("FTP 服务器未明确目录项类型，拒绝危险操作");
      let mode: number | null = null;
      if (!item.isSymbolicLink) {
        const response = await this.client.sendIgnoringError("MLST " + target);
        const match =
          response.code < 400
            ? response.message.match(/(?:^|[;\s])unix\.mode=([0-7]{3,4});/i)
            : null;
        if (match) mode = parseInt(match[1], 8);
      }
      return {
        directory: item.isDirectory,
        link: item.isSymbolicLink,
        mode,
        size: item.size,
        modified: item.modifiedAt?.getTime() ?? 0,
      };
    });
  }
  chmod(file: string, mode: number) {
    return this.run(async () => {
      if (!this.chmodSupported)
        throw new Error("FTP 服务器不支持可确认的 SITE CHMOD");
      if (!Number.isInteger(mode) || mode < 0 || mode > 0o7777)
        throw new Error("权限参数无效");
      await this.client.send(
        `SITE CHMOD ${mode.toString(8)} ${this.resolve(file)}`,
      );
    });
  }
  action(
    kind: "mkdir" | "remove" | "rmdir" | "rename",
    first: string,
    second?: string,
  ) {
    return this.run(async () => {
      const file = this.resolve(first);
      if (kind === "mkdir") await this.client.send("MKD " + file);
      else if (kind === "remove") await this.client.remove(file);
      else if (kind === "rmdir") await this.client.removeEmptyDir(file);
      else await this.client.rename(file, this.resolve(second!));
    });
  }
  transfer(
    direction: "upload" | "download",
    file: string,
    stream: Readable | Writable,
    signal: AbortSignal,
    onProgress: (bytes: number) => void,
  ) {
    return this.run(async () => {
      if (signal.aborted) throw new Error("已取消");
      const abort = () => {
        stream.destroy();
        this.close();
      };
      signal.addEventListener("abort", abort, { once: true });
      this.client.trackProgress((info) => onProgress(info.bytes));
      try {
        if (direction === "upload")
          await this.client.uploadFrom(stream as Readable, this.resolve(file));
        else
          await this.client.downloadTo(stream as Writable, this.resolve(file));
      } finally {
        this.client.trackProgress();
        signal.removeEventListener("abort", abort);
      }
    });
  }
}
