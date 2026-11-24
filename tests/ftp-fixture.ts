import { createServer, type Socket, type Server } from "node:net";
import {
  mkdtemp,
  writeFile,
  readFile,
  readdir,
  stat,
  lstat,
  chmod,
  mkdir,
  rmdir,
  rename,
  unlink,
  rm,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
export async function ftpFixture(
  options: {
    chmod?: boolean;
    denyRemoveNames?: string[];
    unknownTypes?: boolean;
  } = {},
) {
  const directory = await mkdtemp(path.join(tmpdir(), "forterm-ftp-"));
  await writeFile(path.join(directory, "ftp.txt"), "real FTP content\n");
  const peers = new Set<Socket>(),
    dataPeers = new Set<Socket>(),
    servers = new Set<Server>();
  let authentications = 0,
    connections = 0;
  const verbs: string[] = [];
  const server = createServer((socket) => {
    connections++;
    peers.add(socket);
    socket.on("error", () => {});
    socket.on("close", () => peers.delete(socket));
    socket.write("220 synthetic FTP service\r\n");
    let buffer = "",
      cwd = "/",
      renameFrom = "",
      queue = Promise.resolve();
    let passive: { server: Server; connected: Promise<Socket> } | undefined;
    const local = (remote: string) => {
      const normalized = path.posix.resolve(cwd, remote.replace(/^"|"$/g, ""));
      return { normalized, file: path.join(directory, "." + normalized) };
    };
    const reply = (value: string) => socket.write(value + "\r\n");
    async function command(line: string) {
      const split = line.indexOf(" "),
        verb = (split < 0 ? line : line.slice(0, split)).toUpperCase(),
        arg = split < 0 ? "" : line.slice(split + 1);
      verbs.push(verb);
      try {
        if (verb === "USER") reply("331 password needed");
        else if (verb === "PASS") {
          if (arg !== "synthetic-ftp-password") reply("530 denied");
          else {
            authentications++;
            reply("230 logged in");
          }
        } else if (verb === "FEAT")
          socket.write(
            "211-Features\r\n UTF8\r\n MLST type*;size*;modify*;\r\n EPSV\r\n SIZE\r\n MDTM\r\n211 End\r\n",
          );
        else if (["TYPE", "STRU", "OPTS", "NOOP"].includes(verb))
          reply("200 OK");
        else if (verb === "SYST") reply("215 UNIX Type: L8");
        else if (verb === "HELP" && arg === "SITE")
          reply(options.chmod ? "214 CHMOD" : "502 SITE unsupported");
        else if (verb === "MLST") {
          if (!options.chmod) {
            reply("502 mode facts unsupported");
            return;
          }
          const info = await lstat(local(arg).file);
          reply(
            `250 type=${info.isSymbolicLink() ? "OS.unix=symlink" : info.isDirectory() ? "dir" : "file"};unix.mode=${(info.mode & 0o7777).toString(8)}; ${arg}`,
          );
        } else if (verb === "SITE") {
          if (!options.chmod) reply("502 unsupported");
          else {
            const m = arg.match(/^CHMOD ([0-7]{3,4}) (.+)$/);
            if (!m) throw 0;
            await chmod(local(m[2]).file, parseInt(m[1], 8));
            reply("200 permissions changed");
          }
        } else if (verb === "PWD") reply(`257 "${cwd}"`);
        else if (verb === "CWD") {
          if (!(await stat(local(arg).file)).isDirectory()) throw 0;
          cwd = local(arg).normalized;
          reply("250 directory changed");
        } else if (verb === "SIZE")
          reply(`213 ${(await stat(local(arg).file)).size}`);
        else if (verb === "MDTM")
          reply(
            "213 " +
              (await stat(local(arg).file)).mtime
                .toISOString()
                .slice(0, 19)
                .replace(/[-T:]/g, ""),
          );
        else if (verb === "EPSV") {
          const data = createServer();
          servers.add(data);
          const connected = new Promise<Socket>((resolve) =>
            data.once("connection", (peer) => {
              dataPeers.add(peer);
              peer.on("error", () => {});
              peer.on("close", () => dataPeers.delete(peer));
              resolve(peer);
            }),
          );
          await new Promise<void>((resolve) =>
            data.listen(0, "127.0.0.1", resolve),
          );
          passive = { server: data, connected };
          reply(
            `229 Entering Extended Passive Mode (|||${(data.address() as any).port}|)`,
          );
        } else if (["MLSD", "LIST", "RETR", "STOR"].includes(verb)) {
          if (!passive) throw 0;
          const channel = passive;
          passive = undefined;
          reply("150 starting data");
          const peer = await channel.connected;
          if (verb === "STOR") {
            const chunks: Buffer[] = [];
            for await (const chunk of peer) chunks.push(Buffer.from(chunk));
            await writeFile(local(arg).file, Buffer.concat(chunks));
          } else if (verb === "RETR") {
            const bytes = await readFile(local(arg).file);
            await new Promise<void>((resolve) => peer.end(bytes, resolve));
          } else {
            const target = local(arg || cwd);
            const names = await readdir(target.file);
            let text = "";
            for (const name of names) {
              const s = await lstat(path.join(target.file, name));
              text += `type=${options.unknownTypes ? "unknown" : s.isSymbolicLink() ? "OS.unix=symlink" : s.isDirectory() ? "dir" : "file"};size=${s.size};modify=${s.mtime.toISOString().slice(0, 19).replace(/[-T:]/g, "")}; ${name}\r\n`;
            }
            await new Promise<void>((resolve) => peer.end(text, resolve));
          }
          channel.server.close();
          servers.delete(channel.server);
          reply("226 transfer complete");
        } else if (verb === "MKD") {
          await mkdir(local(arg).file);
          reply(`257 "${arg}" created`);
        } else if (verb === "RMD") {
          await rmdir(local(arg).file);
          reply("250 removed");
        } else if (verb === "DELE") {
          if (options.denyRemoveNames?.some((name) => arg.endsWith(name))) {
            reply("550 Permission denied");
            return;
          }
          await unlink(local(arg).file);
          reply("250 removed");
        } else if (verb === "RNFR") {
          renameFrom = local(arg).file;
          reply("350 target needed");
        } else if (verb === "RNTO") {
          await rename(renameFrom, local(arg).file);
          reply("250 renamed");
        } else if (verb === "QUIT") {
          reply("221 bye");
          socket.end();
        } else reply("502 command unsupported");
      } catch {
        reply("550 file operation denied");
      }
    }
    socket.on("data", (bytes) => {
      buffer += bytes.toString();
      for (;;) {
        const end = buffer.indexOf("\r\n");
        if (end < 0) break;
        const line = buffer.slice(0, end);
        buffer = buffer.slice(end + 2);
        queue = queue.then(() => command(line)).catch(() => {});
      }
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  return {
    directory,
    port: (server.address() as any).port as number,
    get authentications() {
      return authentications;
    },
    get connections() {
      return connections;
    },
    verbs,
    async close() {
      for (const socket of peers) socket.destroy();
      for (const peer of dataPeers) peer.destroy();
      for (const data of servers) data.close();
      await new Promise<void>((resolve) => server.close(() => resolve()));
      await rm(directory, { recursive: true, force: true });
    },
  };
}
