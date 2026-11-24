import type { SFTPWrapper, Stats } from "ssh2";
import { createHash, randomUUID } from "node:crypto";
import path from "node:path";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import type { RemoteText } from "../shared/types";
export const TEXT_LIMIT = 1024 * 1024;
const locks = new WeakMap<SFTPWrapper, Set<string>>();
const digest = (data: Buffer, stat: Stats) =>
  createHash("sha256")
    .update(data)
    .update(JSON.stringify([stat.size, stat.mtime, stat.mode]))
    .digest("hex");
const stats = (ftp: SFTPWrapper, file: string) =>
  new Promise<Stats>((resolve, reject) => {
    const timer = setTimeout(
      () => reject(new Error("文件信息读取超时，请保留草稿并检查连接")),
      15000,
    );
    try {
      ftp.lstat(file, (e, stat) => {
        clearTimeout(timer);
        e ? reject(new Error("无法读取文件，请检查连接与权限")) : resolve(stat);
      });
    } catch {
      clearTimeout(timer);
      reject(new Error("文件通道已关闭，请保留草稿"));
    }
  });
export async function readRemoteText(
  ftp: SFTPWrapper,
  file: string,
): Promise<RemoteText> {
  file = path.posix.normalize(file);
  const stat = await stats(ftp, file);
  if (!stat.isFile()) throw new Error("只能编辑普通文件，不支持目录或符号链接");
  if (stat.size > TEXT_LIMIT) throw new Error("在线编辑限 1 MiB，请下载后编辑");
  const stream = ftp.createReadStream(file);
  const timer = setTimeout(
    () => stream.destroy(new Error("文件读取超时，编辑内容未修改")),
    15000,
  );
  const chunks: Buffer[] = [];
  let size = 0;
  try {
    for await (const chunk of stream) {
      size += chunk.length;
      if (size > TEXT_LIMIT) throw new Error("在线编辑限 1 MiB，请下载后编辑");
      chunks.push(Buffer.from(chunk));
    }
  } catch (e) {
    if (e instanceof Error && /[\u4e00-\u9fff]/.test(e.message)) throw e;
    throw new Error("文件读取失败，请检查连接与权限");
  } finally {
    clearTimeout(timer);
    stream.destroy();
  }
  const after = await stats(ftp, file);
  if (
    stat.size !== after.size ||
    stat.mtime !== after.mtime ||
    stat.mode !== after.mode
  )
    throw new Error("读取期间文件已变化，请重新打开");
  const data = Buffer.concat(chunks);
  if (data.some((b) => b === 0 || (b < 32 && ![9, 10, 13].includes(b))))
    throw new Error("检测到二进制内容，不支持在线编辑");
  let text: string;
  try {
    text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(
      data,
    );
  } catch {
    throw new Error("文件不是有效 UTF-8，请下载后使用对应编码编辑");
  }
  return { path: file, text, revision: digest(data, after), mode: after.mode };
}
export async function saveRemoteText(
  ftp: SFTPWrapper,
  file: string,
  text: string,
  revision: string,
): Promise<RemoteText> {
  file = path.posix.normalize(file);
  const data = Buffer.from(text, "utf8");
  if (data.length > TEXT_LIMIT) throw new Error("在线编辑限 1 MiB");
  if (text.includes("\0")) throw new Error("文本不能包含空字节");
  let busy = locks.get(ftp);
  if (!busy) {
    busy = new Set();
    locks.set(ftp, busy);
  }
  if (busy.has(file)) throw new Error("此文件正在保存，请稍后再试");
  busy.add(file);
  const temporary = path.posix.join(
    path.posix.dirname(file),
    ".forterm-edit-" + randomUUID() + ".tmp",
  );
  let created = false;
  try {
    const original = await readRemoteText(ftp, file);
    if (original.revision !== revision)
      throw new Error(
        "保存冲突：远程文件已变化。请保留草稿并重新读取后比较，未覆盖远程文件",
      );
    const output = ftp.createWriteStream(temporary, {
      flags: "wx",
      mode: 0o600,
    });
    created = true;
    await pipeline(Readable.from([data]), output, {
      signal: AbortSignal.timeout(15000),
    });
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(
        () => reject(new Error("恢复文件权限超时，未替换原文件")),
        15000,
      );
      const done = (error?: Error | null) => {
        clearTimeout(timer);
        error
          ? reject(new Error("无法恢复原文件权限，未替换原文件"))
          : resolve();
      };
      try {
        ftp.chmod(temporary, original.mode & 0o777, done);
      } catch {
        done(new Error("closed"));
      }
    });
    const latest = await readRemoteText(ftp, file);
    if (latest.revision !== revision)
      throw new Error("保存冲突：远程文件已变化，未覆盖远程文件");
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(
        () =>
          reject(
            new Error("保存结果未确认，请保留草稿并重新读取远程文件核对内容"),
          ),
        15000,
      );
      const done = (e?: Error | null) => {
        clearTimeout(timer);
        e
          ? reject(
              new Error(
                "保存替换失败，请检查目录权限或服务器是否允许原子替换；原文件未主动删除",
              ),
            )
          : resolve();
      };
      try {
        ftp.ext_openssh_rename(temporary, file, done);
      } catch {
        try {
          ftp.rename(temporary, file, done);
        } catch {
          done(new Error("closed"));
        }
      }
    });
    created = false;
    // Return the saved snapshot; a subsequent remote edit must still conflict.
    const stat = await stats(ftp, file);
    return { path: file, text, revision: digest(data, stat), mode: stat.mode };
  } catch (e) {
    if (e instanceof Error && /[\u4e00-\u9fff]/.test(e.message)) throw e;
    throw new Error("保存失败，请保留草稿并检查连接、文件和目录权限");
  } finally {
    busy.delete(file);
    if (created) ftp.unlink(temporary, () => {});
  }
}

// First explicit save uses exclusive OPEN. Never delete an existing name.
export async function createRemoteText(
  ftp: SFTPWrapper,
  file: string,
  text: string,
): Promise<RemoteText> {
  const bytes = Buffer.from(text, "utf8");
  if (bytes.length > TEXT_LIMIT || text.includes("\0"))
    throw new Error("新文件内容超出文本边界");
  await new Promise<void>((resolve, reject) =>
    ftp.lstat(file, (e) => {
      if (!e) reject(new Error("同名远程文件已存在，未覆盖"));
      else if ((e as any).code === 2) resolve();
      else reject(new Error("无法检查新文件名称与权限"));
    }),
  );
  try {
    await pipeline(
      Readable.from([bytes]),
      ftp.createWriteStream(file, { flags: "wx", mode: 0o600 }),
      { signal: AbortSignal.timeout(15000) },
    );
  } catch {
    throw new Error(
      "新文件创建失败，未覆盖同名文件；首次写入中断可能留下部分新文件，请核对后再操作",
    );
  }
  return readRemoteText(ftp, file);
}
