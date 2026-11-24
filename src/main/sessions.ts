import { FileQueue, FileRequestError } from "./file-queue";
import { FtpFiles } from "./ftp-files";
import {
  Client,
  type ClientChannel,
  type SFTPWrapper,
  type ConnectConfig,
} from "ssh2";
import { randomUUID, createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import type { IPty } from "node-pty";
import type {
  AppEvent,
  Connection,
  SessionInfo,
  RemoteFile,
} from "../shared/types";
import type { Store } from "./store";
import type { Prompts } from "./prompts";
import { connectionSchema } from "./schema";
import { StringDecoder } from "node:string_decoder";
import { SftpHeartbeat } from "./sftp-heartbeat";
import { DEFAULT_KEEP_ALIVE, KEEP_ALIVE_MISSES } from "../shared/keep-alive";
interface Session {
  info: SessionInfo;
  ftp?: FtpFiles;
  clients: Client[];
  client?: Client;
  channel?: ClientChannel;
  shellPromise?: Promise<void>;
  pty?: IPty;
  sftp?: SFTPWrapper;
  sftpPromise?: Promise<SFTPWrapper>;
  sftpEpoch?: number;
  buffer: string;
  attached: boolean;
  disposed: boolean;
  monitorBusy: boolean;
  sftpHeartbeat?: SftpHeartbeat;
}
export class Sessions {
  private sessions = new Map<string, Session>();
  private fileQueue = new FileQueue();
  private fileRequests = new Map<string, AbortController>();
  constructor(
    private emit: (e: AppEvent) => void,
    private store: Store,
    private prompts: Prompts,
    private options: { fileTimeoutMs?: number } = {},
  ) {}
  list() {
    return [...this.sessions.values()].map((s) => ({ ...s.info }));
  }
  markExternalLaunch(id: string, count: number) {
    const s = this.sessions.get(id);
    if (!s) return;
    s.info.launchInvocations = count;
    this.emit({ type: "session", session: { ...s.info } });
  }
  private state(s: Session, status: SessionInfo["status"], message?: string) {
    s.info.status = status;
    s.info.message = message;
    this.emit({ type: "session", session: { ...s.info } });
  }
  private data(s: Session, data: string) {
    s.buffer = (s.buffer + data).slice(-1024 * 1024);
    if (s.attached) this.emit({ type: "data", id: s.info.id, data });
  }
  get(id: string) {
    const s = this.sessions.get(id);
    if (!s || s.disposed) throw new Error("会话已关闭");
    return s;
  }
  attach(id: string) {
    const s = this.sessions.get(id);
    if (!s) throw new Error("会话已关闭");
    s.attached = true;
    return s.buffer;
  }
  private create(
    protocol: SessionInfo["protocol"],
    title: string,
    host: string,
    temporary: boolean,
  ) {
    const info: SessionInfo = {
      id: randomUUID(),
      title,
      host,
      protocol,
      temporary,
      status: "connecting",
      keepAlive:
        protocol === "local" || protocol === "ftp"
          ? undefined
          : this.store.keepAlive(),
    };
    const s: Session = {
      info,
      clients: [],
      buffer: "",
      attached: false,
      disposed: false,
      monitorBusy: false,
    };
    this.sessions.set(info.id, s);
    this.state(s, "connecting");
    return s;
  }
  async connect(input: Connection) {
    const c = connectionSchema.parse(input);
    const s = this.create(c.protocol, c.title || c.host, c.host, !!c.temporary);
    if (c.protocol === "ftp") {
      const ftp = new FtpFiles(() => {
        if (!s.disposed && s.info.status !== "error")
          this.state(
            s,
            "closed",
            "FTP 文件连接已结束；终端与其他文件连接保持可用",
          );
      });
      s.ftp = ftp;
      void ftp
        .open(c)
        .then(() => {
          if (s.disposed) ftp.close();
          else
            this.state(
              s,
              "ready",
              "FTP 被动模式；明文传输。串行文件操作，取消传输会关闭此文件连接",
            );
        })
        .catch(() => {
          if (!s.disposed) {
            this.state(
              s,
              "error",
              "FTP 连接失败，请检查文件连接的独立认证；未影响终端",
            );
            ftp.close();
          }
        });
    } else void this.openSSH(s, c);
    return s.info.id;
  }
  private async dial(
    s: Session,
    c: Connection,
    sock?: NodeJS.ReadWriteStream,
  ): Promise<Client> {
    if (s.disposed) throw new Error("会话已关闭");
    const client = new Client();
    s.clients.push(client);
    let password = c.password;
    let privateKey: Buffer | undefined;
    if (c.keyPath) privateKey = await readFile(c.keyPath);
    if (password === undefined && !privateKey) {
      const answer = await this.prompts.ask({
        kind: "auth",
        title: "SSH 密码",
        text: `${c.host}:${c.port}`,
        fields: [{ label: "密码", secret: true }],
      });
      if (!answer) throw new Error("认证已取消");
      password = answer[0];
    }
    if (s.disposed) throw new Error("会话已关闭");
    return new Promise((resolve, reject) => {
      let settled = false;
      const fail = (e: Error) => {
        if (!settled) {
          settled = true;
          reject(e);
        } else if (!s.disposed) {
          this.state(
            s,
            "error",
            e.message === "Keepalive timeout"
              ? "会话心跳连续未获响应，连接已断开；临时会话请回到控制器重新授权"
              : "SSH 连接已中断；临时会话请回到控制器重新授权",
          );
          this.cleanup(s);
        }
      };
      client.on("error", fail);
      client.once("ready", () => {
        if (s.disposed) {
          client.end();
          return;
        }
        settled = true;
        resolve(client);
      });
      client.once("close", () => {
        if (!settled) {
          settled = true;
          reject(new Error("连接已关闭"));
        } else if (
          !s.disposed &&
          client === s.client &&
          s.info.status !== "error"
        ) {
          this.state(s, "closed", "连接已结束");
          this.cleanup(s);
        }
      });
      client.on(
        "keyboard-interactive",
        (_name, _instructions, _lang, questions, finish) => {
          void this.prompts
            .ask({
              kind: "auth",
              title: "交互认证",
              text: `${c.host}:${c.port}`,
              fields: questions.map((q) => ({
                label: q.prompt.slice(0, 256),
                secret: !q.echo,
              })),
            })
            .then((a) => {
              if (a && !s.disposed) finish(a);
              else {
                client.end();
                fail(new Error("认证已取消"));
              }
            });
        },
      );
      const keepAlive = s.info.keepAlive ?? DEFAULT_KEEP_ALIVE;
      const config: ConnectConfig = {
        host: c.host,
        port: c.port,
        username: c.username,
        password,
        privateKey,
        passphrase: c.passphrase,
        sock: sock as ConnectConfig["sock"],
        tryKeyboard: true,
        readyTimeout: 120000,
        keepaliveInterval: keepAlive.enabled
          ? keepAlive.intervalSeconds * 1000
          : 0,
        keepaliveCountMax: KEEP_ALIVE_MISSES,
        hostVerifier: (key: Buffer, verify: (ok: boolean) => void) => {
          const fingerprint =
            "SHA256:" +
            createHash("sha256")
              .update(key)
              .digest("base64")
              .replace(/=+$/, "");
          const known = this.store.host(c.host, c.port);
          if (known === fingerprint) {
            verify(true);
            return;
          }
          void this.prompts
            .ask({
              kind: "host",
              title: known ? "服务器指纹发生变化" : "首次连接：确认服务器指纹",
              text: `${c.host}:${c.port}\n${fingerprint}${known ? "\n之前：" + known : ""}`,
              fields: [],
              changed: !!known,
            })
            .then((a) => {
              if (a && !s.disposed) {
                this.store.trust(c.host, c.port, fingerprint);
                verify(true);
              } else verify(false);
            })
            .catch(() => verify(false));
        },
      };
      try {
        client.connect(config);
      } catch (e) {
        fail(e as Error);
      }
    });
  }
  private async chain(
    c: Connection,
    seen = new Set<string>(),
  ): Promise<Connection[]> {
    if (!c.jumpId) return [c];
    if (seen.has(c.jumpId) || seen.size >= 8) throw new Error("跳板链无效");
    seen.add(c.jumpId);
    return [...(await this.chain(await this.store.resolve(c.jumpId), seen)), c];
  }
  private async openSSH(s: Session, initial: Connection) {
    try {
      const chain = await this.chain(initial);
      let previous: Client | undefined;
      for (const c of chain) {
        let sock: NodeJS.ReadWriteStream | undefined;
        if (previous)
          sock = await new Promise((resolve, reject) =>
            previous!.forwardOut("127.0.0.1", 0, c.host, c.port, (e, ch) =>
              e ? reject(e) : resolve(ch),
            ),
          );
        previous = await this.dial(s, c, sock);
      }
      s.client = previous;
      if (s.disposed) {
        this.cleanup(s);
        return;
      }
      if (initial.protocol === "sftp") {
        this.state(s, "ready");
        await this.sftp(s.info.id).catch(() => {
          this.state(s, "ready", "文件通道未授权；可尝试打开终端");
        });
        return;
      }
      this.state(s, "ready");
      await this.openTerminal(s.info.id).catch(() => {});
    } catch {
      if (!s.disposed) {
        this.state(
          s,
          "error",
          "连接失败：请检查入口、认证或服务器指纹。临时会话请通过控制器重新授权。",
        );
        this.cleanup(s);
      }
    } finally {
      initial.password = undefined;
      initial.passphrase = undefined;
    }
  }
  async openTerminal(id: string): Promise<void> {
    const s = this.get(id);
    if (s.channel || s.pty) return;
    if (!s.client || s.info.status !== "ready")
      throw new Error("SSH 连接尚未就绪");
    if (s.shellPromise) return s.shellPromise;
    s.info.terminalStatus = "opening";
    this.state(s, "ready");
    s.shellPromise = (async () => {
      try {
        const ch = await new Promise<ClientChannel>((resolve, reject) =>
          s.client!.shell(
            { term: "xterm-256color", cols: 100, rows: 30 },
            (e, channel) => (e ? reject(e) : resolve(channel)),
          ),
        );
        if (s.disposed) {
          ch.end();
          throw new Error("会话已关闭");
        }
        s.channel = ch;
        const decoder = new StringDecoder("utf8"),
          errors = new StringDecoder("utf8");
        ch.on("data", (b: Buffer) => this.data(s, decoder.write(b)));
        ch.stderr.on("data", (b: Buffer) => this.data(s, errors.write(b)));
        const ended = (failed: boolean) => {
          if (s.channel !== ch || s.disposed) return;
          this.data(s, decoder.end() + errors.end());
          s.channel = undefined;
          s.info.terminalStatus = failed ? "error" : "closed";
          s.info.terminalMessage = failed
            ? "终端通道中断；文件通道仍可使用"
            : "终端已结束；文件通道仍可使用";
          this.state(s, "ready");
        };
        ch.on("close", () => ended(false));
        ch.on("error", () => ended(true));
        s.info.terminalStatus = "ready";
        s.info.terminalMessage = undefined;
        this.state(s, "ready");
      } catch {
        if (!s.disposed) {
          s.info.terminalStatus = "error";
          s.info.terminalMessage =
            "服务器未开放终端通道；可继续使用文件通道，无需重新认证";
          this.state(s, "ready");
        }
        throw new Error("终端通道不可用；已有文件连接保持可用");
      }
    })();
    try {
      await s.shellPromise;
    } finally {
      s.shellPromise = undefined;
    }
  }
  async local(shell: "powershell" | "cmd" | "default" = "default") {
    const s = this.create(
      "local",
      shell === "cmd"
        ? "CMD"
        : process.platform === "win32"
          ? "PowerShell"
          : "本地终端",
      "localhost",
      false,
    );
    try {
      const pty = await import("node-pty");
      const executable =
        process.platform === "win32"
          ? shell === "cmd"
            ? "cmd.exe"
            : "powershell.exe"
          : process.env.SHELL || "/bin/zsh";
      s.pty = pty.spawn(executable, [], {
        name: "xterm-256color",
        cols: 100,
        rows: 30,
        cwd: process.env.USERPROFILE || process.env.HOME,
        env: process.env as Record<string, string>,
      });
      s.pty.onData((d) => this.data(s, d));
      s.pty.onExit(() => {
        if (!s.disposed) {
          this.state(s, "closed", "本地终端已退出");
          this.cleanup(s);
        }
      });
      this.state(s, "ready");
    } catch {
      this.state(
        s,
        "error",
        "本地终端启动失败，请检查 node-pty 与系统终端安装",
      );
    }
    return s.info.id;
  }
  input(id: string, data: string) {
    const s = this.get(id);
    if (s.info.status !== "ready") return;
    if (s.pty) s.pty.write(data);
    else s.channel?.write(data);
  }
  resize(id: string, cols: number, rows: number) {
    const s = this.get(id);
    if (s.info.status !== "ready") return;
    s.pty?.resize(cols, rows);
    s.channel?.setWindow(rows, cols, 0, 0);
  }
  private cleanup(s: Session) {
    s.disposed = true;
    s.ftp?.close();
    s.sftpHeartbeat?.stop();
    s.sftpHeartbeat = undefined;
    s.sftp?.end();
    s.channel?.end();
    s.pty?.kill();
    for (const c of [...s.clients].reverse()) c.end();
    s.clients = [];
    s.client = undefined;
    s.channel = undefined;
    s.sftp = undefined;
    s.pty = undefined;
  }
  close(id: string) {
    const s = this.sessions.get(id);
    if (s) {
      if (!s.disposed) this.cleanup(s);
      this.sessions.delete(id);
      this.emit({ type: "session-removed", id });
    }
  }
  closeAll() {
    for (const id of this.sessions.keys()) this.close(id);
  }
  fileTask<T>(
    id: string,
    run: () => Promise<T>,
    signal?: AbortSignal,
    kind: "metadata" | "transfer" = "metadata",
  ): Promise<T> {
    try {
      this.get(id);
    } catch (e) {
      return Promise.reject(e);
    }
    return this.fileQueue.run(id, run, signal, kind);
  }
  fileBusy(id: string) {
    return this.fileQueue.busy(id);
  }
  cancelFileRequest(id: string, requestId: string) {
    this.fileRequests.get(id + ":" + requestId)?.abort();
  }
  resetFileChannel(id: string) {
    const s = this.sessions.get(id);
    if (!s) return;
    s.sftpHeartbeat?.stop();
    s.sftpHeartbeat = undefined;
    s.sftpEpoch = (s.sftpEpoch ?? 0) + 1;
    const channel = s.sftp;
    s.sftp = undefined;
    s.sftpPromise = undefined;
    if (channel) {
      try {
        channel.destroy();
      } catch {
        channel.end();
      }
    }
    s.ftp?.close();
  }
  fileCall<T>(
    id: string,
    label: string,
    invoke: (
      done: (error: Error | null | undefined, result?: T) => void,
    ) => void,
    signal?: AbortSignal,
  ): Promise<T> {
    return new Promise((resolve, reject) => {
      let settled = false;
      const finish = (error?: Error, result?: T) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        signal?.removeEventListener("abort", abort);
        error ? reject(error) : resolve(result!);
      };
      const abort = () => {
        finish(new FileRequestError(label + "已取消"));
        this.resetFileChannel(id);
      };
      const timer = setTimeout(() => {
        finish(new FileRequestError(label+"超时，文件通道已重置；终端保持可用"));
        this.resetFileChannel(id);
      }, this.options.fileTimeoutMs ?? 10000);
      signal?.addEventListener("abort", abort, { once: true });
      if (signal?.aborted) {
        abort();
        return;
      }
      try {
        invoke((error, result) => {
          if (error) {
            const code = (error as any).code;
            finish(
              new FileRequestError(
                label +
                  (code === 2
                    ? "：路径不存在"
                    : code === 3
                      ? "：权限不足"
                      : code === 8
                        ? "：服务器不支持或未授权"
                        : "失败，请检查文件连接状态"),
              ),
            );
          } else finish(undefined, result);
        });
      } catch {
        finish(new FileRequestError(label + "失败：文件连接已断开"));
      }
    });
  }
  ftp(id: string): FtpFiles | undefined {
    return this.get(id).ftp;
  }
  async remoteExists(id: string, file: string): Promise<boolean> {
    const ftp = this.get(id).ftp;
    if (ftp) return ftp.exists(file);
    const channel = await this.sftp(id);
    return this.fileCall<boolean>(id, "检查远程文件", (done) =>
      channel.stat(file, (e) => {
        if (!e) done(null, true);
        else if ((e as any).code === 2) done(null, false);
        else done(e);
      }),
    );
  }
  async sftp(id: string): Promise<SFTPWrapper> {
    const s = this.get(id);
    if (s.ftp)
      throw new Error(
        "FTP 不是 SFTP；当前 FTP 不支持安全在线编辑，请下载修改后上传",
      );
    if (!s.client) throw new Error("SSH 连接尚未就绪");
    if (s.sftp) return s.sftp;
    if (s.sftpPromise) return s.sftpPromise;
    const epoch = s.sftpEpoch ?? 0;
    s.sftpPromise = new Promise<SFTPWrapper>((resolve, reject) => {
      const timer = setTimeout(() => {
        s.sftpEpoch = (s.sftpEpoch ?? 0) + 1;
        reject(new Error("SFTP 通道打开超时，终端保持可用"));
      }, this.options.fileTimeoutMs ?? 10000);
      s.client!.sftp((err, ftp) => {
        clearTimeout(timer);
        if (epoch !== (s.sftpEpoch ?? 0)) {
          ftp?.end();
          reject(new Error("旧 SFTP 通道请求已取消"));
          return;
        }
        if (err) {
          reject(new Error("此会话不支持或未授权 SFTP"));
          return;
        }
        if (s.disposed) {
          ftp.end();
          reject(new Error("会话已关闭"));
          return;
        }
        s.sftp = ftp;
        const keepAlive = s.info.keepAlive ?? DEFAULT_KEEP_ALIVE;
        if (keepAlive.enabled && keepAlive.sftpProbe) {
          s.sftpHeartbeat = new SftpHeartbeat(
            keepAlive.intervalSeconds * 1000,
            () => {
              if (this.fileBusy(id)) return Promise.resolve();
              return this.fileTask(id, () =>
                this.fileCall<void>(id, "SFTP 只读心跳", (done) => {
                  if (s.disposed || s.sftp !== ftp) {
                    done(null);
                    return;
                  }
                  ftp.realpath(".", (error) => done(error));
                }),
              );
            },
          );
        }
        const stopHeartbeat = () => {
          if (s.sftp !== ftp) return;
          s.sftpHeartbeat?.stop();
          s.sftpHeartbeat = undefined;
          s.sftp = undefined;
        };
        ftp.on("error", () => {
          stopHeartbeat();
        });
        ftp.on("close", () => {
          stopHeartbeat();
        });
        resolve(ftp);
      });
    });
    try {
      return await s.sftpPromise;
    } finally {
      s.sftpPromise = undefined;
    }
  }
  async listFiles(id: string, directory: string, requestId?: string) {
    const controller = new AbortController(),
      key = requestId ? id + ":" + requestId : undefined;
    if (key) this.fileRequests.set(key, controller);
    try {
      return await this.fileTask(
        id,
        async () => {
          const independent = this.get(id).ftp;
          if (independent) {
            const abort = () => independent.close();
            controller.signal.addEventListener("abort", abort, { once: true });
            try {
              return await independent.list(directory);
            } finally {
              controller.signal.removeEventListener("abort", abort);
            }
          }
          const channel = await this.sftp(id);
          const resolved = await this.fileCall<string>(
            id,
            "目录路径解析",
            (done) => channel.realpath(directory, (e, p) => done(e, p)),
            controller.signal,
          );
          const entries = await this.fileCall<any[]>(
            id,
            "目录读取",
            (done) => channel.readdir(resolved, (e, list) => done(e, list)),
            controller.signal,
          );
          return {
            path: resolved,
            files: entries
              .filter((f) => f.filename !== "." && f.filename !== "..")
              .map((f) => ({
                name: f.filename,
                directory: f.attrs.isDirectory(),
                size: f.attrs.size,
                modified: f.attrs.mtime,
                mode: f.attrs.mode,
              }))
              .sort(
                (a, b) =>
                  Number(b.directory) - Number(a.directory) ||
                  a.name.localeCompare(b.name),
              ),
          };
        },
        controller.signal,
      );
    } finally {
      if (key) this.fileRequests.delete(key);
    }
  }
  async fileAction(
    id: string,
    action: "mkdir" | "remove" | "rmdir" | "rename",
    first: string,
    second?: string,
  ) {
    if (this.get(id).ftp) {
      await this.get(id).ftp!.action(action, first, second);
      return;
    }
    const ftp=await this.sftp(id);
    await this.fileCall<void>(id,action==="mkdir"?"创建目录":action==="rename"?"重命名":"文件操作",done=>{if(action==="rename")ftp.rename(first,second!,done);else if(action==="remove")ftp.unlink(first,done);else if(action==="rmdir")ftp.rmdir(first,done);else ftp.mkdir(first,done);});
  }

  async monitor(id: string) {
    const s = this.get(id);
    if (!s.client) throw new Error("当前会话不支持远程监控");
    if (s.monitorBusy) throw new Error("监控正在读取");
    s.monitorBusy = true;
    try {
      return await new Promise<string>((resolve, reject) => {
        let finished = false;
        const timer = setTimeout(() => {
          done(new Error("监控超时或堡垒机禁止命令通道"));
          channel?.close();
        }, 8000);
        let output = "";
        let channel: ClientChannel | undefined;
        const done = (e?: Error) => {
          if (finished) return;
          finished = true;
          clearTimeout(timer);
          e
            ? reject(e)
            : output.trim()
              ? resolve(output)
              : reject(new Error("监控未返回数据，请检查远程系统与命令权限"));
        };
        s.client!.exec(
          "printf '负载（1 / 5 / 15 分钟）\\n'; cat /proc/loadavg; printf '\\nCPU 累计计数（USER_HZ）\\n'; head -1 /proc/stat; printf '\\n内存（KiB）\\n'; head -5 /proc/meminfo; printf '\\n磁盘\\n'; df -h /; printf '\\n网络累计字节 / 数据包\\n'; cat /proc/net/dev",
          (err, ch) => {
            if (err) {
              done(new Error("堡垒机未开放监控命令通道"));
              return;
            }
            if (finished || s.disposed) {
              ch.close();
              return;
            }
            channel = ch;
            ch.on("data", (b: Buffer) => {
              output = (output + b.toString()).slice(0, 32768);
            });
            ch.on("close", () => done());
            ch.on("error", () => done(new Error("监控读取失败")));
          },
        );
      });
    } finally {
      s.monitorBusy = false;
    }
  }
}
