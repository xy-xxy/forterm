import { Server, utils } from "ssh2";
import { generateKeyPairSync } from "node:crypto";
import * as fs from "node:fs";
import path from "node:path";
import { tmpdir } from "node:os";
import { setTimeout as delay } from "node:timers/promises";
import { connect } from "node:net";
export async function waitFor(check: () => boolean, ms = 6000) {
  const start = Date.now();
  while (!check()) {
    if (Date.now() - start > ms) throw new Error("Condition timed out");
    await delay(20);
  }
}
export async function fixture(
  options: {
    sftp?: boolean;
    maxOpenFiles?: number;
    writeDelayMs?: number;
    statDelayMs?: number;
    denyRealpathWhileOpen?: boolean;
    ignoreRealpath?: boolean;
    denyRealpath?: boolean;
    stallWriteNames?: string[];
    denyRemoveNames?: string[];
    malformedEntries?: string[];
    shell?: boolean;
    shellDelay?: number;
    banner?: string;
    exec?: boolean;
    write?: boolean;
    rename?: boolean;
    chmod?: boolean;
    onWrite?: () => void;
    interactive?: boolean;
  } = {},
) {
  const directory = fs.mkdtempSync(path.join(tmpdir(), "forterm-ssh-"));
  fs.writeFileSync(path.join(directory, "hello.txt"), "hello 文件\n");
  const key = generateKeyPairSync("rsa", {
    modulusLength: 2048,
  }).privateKey.export({ type: "pkcs1", format: "pem" });
  const peers = new Set<any>();
  let sftpRequests = 0;
  let readOpenRequests = 0;
  let realpathRequests = 0;
  let keepaliveRequests = 0;
  let received = "";
  let openFiles = 0,
    openingFiles = 0,
    maxOpenFiles = 0,
    deniedOpen = 0,
    deniedRealpath = 0;
  let authentications = 0;
  let shellRequests = 0;
  const shellInputs: string[] = [];
  const shellStreams: any[] = [];
  const resizes: { cols: number; rows: number }[] = [];
  let execRequests = 0;
  const server = new Server(
    {
      hostKeys: [key],
      debug: (message: string) => {
        if (
          message.includes("Inbound: GLOBAL_REQUEST") &&
          message.includes("keepalive@openssh.com")
        )
          keepaliveRequests++;
      },
    },
    (client) => {
      peers.add(client);
      client.on("error", () => {});
      client.on("close", () => peers.delete(client));
      client.on("tcpip", (accept, reject, info) => {
        if (info.destIP !== "127.0.0.1") {
          reject();
          return;
        }
        const socket = connect(info.destPort, info.destIP, () => {
          const channel = accept();
          socket.pipe(channel).pipe(socket);
          channel.on("close", () => socket.destroy());
        });
        socket.on("error", () => reject());
      });
      client.on("authentication", (ctx) => {
        if (ctx.method !== "none") authentications++;
        if (options.interactive) {
          if (ctx.method === "keyboard-interactive")
            ctx.prompt(
              [{ prompt: "Verification code: ", echo: false }],
              (answers) =>
                answers[0] === "123456" ? ctx.accept() : ctx.reject(),
            );
          else ctx.reject(["keyboard-interactive"]);
        } else if (
          ctx.method === "password" &&
          ctx.username === "ticket" &&
          ctx.password === "one-time@password"
        )
          ctx.accept();
        else ctx.reject();
      });
      client.on("ready", () =>
        client.on("session", (accept) => {
          const session = accept();
          session.on("pty", (accept) => accept?.());
          session.on("window-change", (accept, _reject, info) => {
            resizes.push({ cols: info.cols, rows: info.rows });
            accept?.();
          });
          session.on("shell", (accept, reject) => {
            shellRequests++;
            if (options.shell === false) {
              reject();
              return;
            }
            const openShell = () => {
              const index = shellInputs.length;
              shellInputs.push("");
              const stream = accept();
              shellStreams[index] = stream;
              stream.write(Buffer.from([0xe4]));
              setTimeout(() => {
                stream.write(
                  Buffer.from([0xbd, 0xa0, 0xe5, 0xa5, 0xbd, 0x0d, 0x0a]),
                );
                if (options.banner) stream.write(options.banner);
              }, 10);
              stream.on("data", (b: Buffer) => {
                received += b.toString();
                shellInputs[index] += b.toString();
                stream.write(b);
              });
            };
            if (options.shellDelay) setTimeout(openShell, options.shellDelay);
            else openShell();
          });
          session.on("exec", (accept, reject) => {
            execRequests++;
            if (options.exec === false) {
              reject();
              return;
            }
            const stream = accept();
            stream.write(
              "LOAD\n0.01 0.02 0.03\nMEMORY_KB\nMemTotal: 1024 kB\n",
            );
            stream.exit(0);
            stream.end();
          });
          session.on("sftp", (accept, reject) => {
            sftpRequests++;
            if (options.sftp === false) {
              reject();
              return;
            }
            const ftp = accept();
            const handles = new Map<
              string,
              { file?: number; name?: string; dir?: string; read?: boolean }
            >();
            let count = 0;
            const code = utils.sftp.STATUS_CODE;
            const status = (id: number, e?: NodeJS.ErrnoException | null) =>
              ftp.status(
                id,
                e
                  ? e.code === "ENOENT"
                    ? code.NO_SUCH_FILE
                    : code.FAILURE
                  : code.OK,
              );
            const local = (p: string) => {
              const full = path.resolve(
                directory,
                "." + (p.startsWith("/") ? p : "/" + p),
              );
              if (!full.startsWith(directory + path.sep) && full !== directory)
                throw new Error("outside fixture");
              return full;
            };
            const attrs = (s: fs.Stats) => ({
              mode: s.mode,
              uid: s.uid,
              gid: s.gid,
              size: s.size,
              atime: Math.floor(s.atimeMs / 1000),
              mtime: Math.floor(s.mtimeMs / 1000),
            });
            ftp.on("REALPATH", (id, p) => {
              realpathRequests++;
              if (options.ignoreRealpath) return;
              if (
                options.denyRealpath ||
                (options.denyRealpathWhileOpen && openFiles > 0)
              ) {
                deniedRealpath++;
                ftp.status(id, code.FAILURE);
                return;
              }
              ftp.name(id, [
                {
                  filename: p === "." ? "/" : p,
                  longname: p,
                  attrs: attrs(fs.statSync(directory)),
                },
              ]);
            });
            for (const name of ["STAT", "LSTAT"])
              ftp.on(name, (id: number, p: string) =>
                setTimeout(
                  () =>
                    (name === "LSTAT" ? fs.lstat : fs.stat)(
                      local(p),
                      (e, s) => {
                        try {
                          e ? status(id, e) : ftp.attrs(id, attrs(s));
                        } catch {}
                      },
                    ),
                  options.statDelayMs ?? 0,
                ),
              );
            ftp.on("OPENDIR", (id, p) => {
              const h = String(++count);
              handles.set(h, { dir: local(p) });
              ftp.handle(id, Buffer.from(h));
            });
            ftp.on("READDIR", (id, h) => {
              const item = handles.get(h.toString());
              if (!item?.dir) return status(id, new Error() as any);
              if (item.read) return ftp.status(id, code.EOF);
              item.read = true;
              try {
                ftp.name(
                  id,
                  [
                    ...fs.readdirSync(item.dir),
                    ...(options.malformedEntries ?? []),
                  ].map((name) => ({
                    filename: name,
                    longname: name,
                    attrs: attrs(
                      fs.lstatSync(
                        options.malformedEntries?.includes(name)
                          ? item.dir!
                          : path.join(item.dir!, name),
                      ),
                    ),
                  })),
                );
              } catch (e) {
                status(id, e as any);
              }
            });
            ftp.on("OPEN", (id, p, flags, attrs) => {
              if (flags === 1) readOpenRequests++;
              if (options.write === false && flags !== 1) {
                ftp.status(id, code.PERMISSION_DENIED);
                return;
              }
              if (
                openFiles + openingFiles >=
                (options.maxOpenFiles ?? Infinity)
              ) {
                deniedOpen++;
                ftp.status(id, code.FAILURE);
                return;
              }
              openingFiles++;
              fs.open(
                local(p),
                utils.sftp.flagsToString(flags)!,
                attrs.mode ?? 0o666,
                (e, fd) => {
                  openingFiles--;
                  if (e) return status(id, e);
                  openFiles++;
                  maxOpenFiles = Math.max(maxOpenFiles, openFiles);
                  const h = String(++count);
                  handles.set(h, { file: fd, name: p });
                  ftp.handle(id, Buffer.from(h));
                },
              );
            });
            ftp.on("FSTAT", (id, h) => {
              const fd = handles.get(h.toString())?.file;
              if (fd === undefined) return status(id, new Error() as any);
              fs.fstat(fd, (e, s) =>
                e ? status(id, e) : ftp.attrs(id, attrs(s)),
              );
            });
            ftp.on("READ", (id, h, offset, length) => {
              const fd = handles.get(h.toString())?.file;
              if (fd === undefined) return status(id, new Error() as any);
              const b = Buffer.alloc(Math.min(length, 65536));
              fs.read(fd, b, 0, b.length, offset, (e, n) =>
                e
                  ? status(id, e)
                  : n
                    ? ftp.data(id, b.subarray(0, n))
                    : ftp.status(id, code.EOF),
              );
            });
            ftp.on("WRITE", (id, h, offset, data) => {
              options.onWrite?.();
              const fd = handles.get(h.toString())?.file;
              if (fd === undefined) return status(id, new Error() as any);
              const item = handles.get(h.toString());
              if (
                options.stallWriteNames?.some((name) =>
                  item?.name?.endsWith(name),
                )
              )
                return;
              setTimeout(
                () =>
                  fs.write(fd, data, 0, data.length, offset, (e) => {
                    try {
                      status(id, e);
                    } catch {}
                  }),
                options.writeDelayMs ?? 0,
              );
            });
            ftp.on("CLOSE", (id, h) => {
              const item = handles.get(h.toString());
              handles.delete(h.toString());
              if (item?.file !== undefined)
                fs.close(item.file, (e) => {
                  openFiles--;
                  try {
                    status(id, e);
                  } catch {}
                });
              else status(id);
            });
            ftp.on("SETSTAT", (id, p, attrs) => {
              if (options.chmod === false) {
                ftp.status(id, code.PERMISSION_DENIED);
                return;
              }
              fs.chmod(local(p), attrs.mode ?? 0o600, (error) =>
                status(id, error),
              );
            });
            ftp.on("MKDIR", (id, p) =>
              fs.mkdir(local(p), (e) => status(id, e)),
            );
            ftp.on("RMDIR", (id, p) =>
              fs.rmdir(local(p), (e) => status(id, e)),
            );
            ftp.on("REMOVE", (id, p) =>
              options.denyRemoveNames?.some((name) => p.endsWith(name))
                ? ftp.status(id, code.PERMISSION_DENIED)
                : fs.unlink(local(p), (e) => status(id, e)),
            );
            ftp.on("RENAME", (id, a, b) => {
              if (options.rename === false) {
                ftp.status(id, code.PERMISSION_DENIED);
                return;
              }
              fs.rename(local(a), local(b), (e) => status(id, e));
            });
            ftp.on("close", () => {
              for (const h of handles.values())
                if (h.file !== undefined)
                  try {
                    fs.closeSync(h.file);
                    openFiles--;
                  } catch {}
            });
          });
        }),
      );
    },
  );
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const port = (server.address() as any).port as number;
  return {
    port,
    directory,
    setBehavior(next: Partial<typeof options>) {
      Object.assign(options, next);
    },
    get openFiles() {
      return openFiles;
    },
    get maxOpenFiles() {
      return maxOpenFiles;
    },
    get deniedOpen() {
      return deniedOpen;
    },
    get deniedRealpath() {
      return deniedRealpath;
    },
    dropConnections() {
      for (const c of peers) c.end();
    },
    get authentications() {
      return authentications;
    },
    get resizes() {
      return resizes;
    },
    writeShell(index: number, data: string) {
      shellStreams[index]?.write(data);
    },
    get shellInputs() {
      return [...shellInputs];
    },
    get shellRequests() {
      return shellRequests;
    },
    get execRequests() {
      return execRequests;
    },
    get readOpenRequests() {
      return readOpenRequests;
    },
    get sftpRequests() {
      return sftpRequests;
    },
    get realpathRequests() {
      return realpathRequests;
    },
    get keepaliveRequests() {
      return keepaliveRequests;
    },
    get received() {
      return received;
    },
    async close() {
      for (const c of peers) c.end();
      await new Promise<void>((resolve) => server.close(() => resolve()));
      fs.rmSync(directory, { recursive: true, force: true });
    },
  };
}
