import { test } from "node:test";
import assert from "node:assert/strict";
import {
  mkdtempSync,
  writeFileSync,
  mkdirSync,
  rmSync,
  existsSync,
  symlinkSync,
  readFileSync,
  chmodSync,
  statSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { Store } from "../src/main/store";
import { Sessions } from "../src/main/sessions";
import { Prompts } from "../src/main/prompts";
import { FileOperations, safeOperationPath } from "../src/main/file-operations";
import { createRemoteText } from "../src/main/remote-text";
import { fixture, waitFor } from "./fixture";
import { ftpFixture } from "./ftp-fixture";
function harness(
  options: { blocked?: () => boolean; onEvent?: (e: any) => void } = {},
) {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-ops-")),
    store = new Store(dir, {
      encrypt: async (s) => s,
      decrypt: async (s) => s,
    }),
    prompts = new Prompts((e) => {
      if (e.type === "prompt")
        queueMicrotask(() => prompts.answer(e.prompt.id, []));
    }),
    sessions = new Sessions(() => {}, store, prompts, { fileTimeoutMs: 500 });
  const events: any[] = [];
  const operations = new FileOperations(
    sessions,
    (e) => {
      events.push(e);
      options.onEvent?.(e);
    },
    () => options.blocked?.() ?? false,
  );
  return {
    dir,
    sessions,
    operations,
    events,
    close() {
      operations.close();
      sessions.closeAll();
      prompts.close();
      rmSync(dir, { recursive: true, force: true });
    },
  };
}
async function ssh(h: ReturnType<typeof harness>, port: number) {
  const id = await h.sessions.connect({
    protocol: "sftp",
    host: "127.0.0.1",
    port,
    username: "ticket",
    password: "one-time@password",
  });
  await waitFor(
    () => h.sessions.list().find((s) => s.id === id)?.status === "ready",
  );
  return id;
}
test("nested SFTP delete unlinks links without following targets and protects root/traversal", async () => {
  const f = await fixture(),
    h = harness();
  try {
    const id = await ssh(h, f.port);
    mkdirSync(path.join(f.directory, "tree/sub"), { recursive: true });
    writeFileSync(path.join(f.directory, "tree/sub/a"), "content");
    const outside = path.join(h.dir, "outside");
    writeFileSync(outside, "keep");
    if (process.platform !== "win32")
      symlinkSync(outside, path.join(f.directory, "tree/link"));
    await h.operations.remove(id, "/tree");
    assert.equal(existsSync(path.join(f.directory, "tree")), false);
    assert.equal(readFileSync(outside, "utf8"), "keep");
    for (const bad of [
      "/",
      "",
      ".",
      "..",
      "/tree/..",
      "/tree/../hello.txt",
      "/tree\\..\\hello.txt",
      "//hello.txt",
      "relative",
    ]) {
      assert.throws(() => safeOperationPath(bad));
    }
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      "hello 文件\n",
    );
  } finally {
    h.close();
    await f.close();
  }
});
test("delete cancellation, permission failure and malicious entries report partial progress", async () => {
  const f = await fixture({ denyRemoveNames: ["b"] }),
    h = harness();
  try {
    const id = await ssh(h, f.port);
    mkdirSync(path.join(f.directory, "tree"));
    writeFileSync(path.join(f.directory, "tree/a"), "a");
    writeFileSync(path.join(f.directory, "tree/b"), "b");
    await assert.rejects(() => h.operations.remove(id, "/tree"), /权限/);
    assert.equal(h.operations.list().at(-1)?.count, 1);
    assert.equal(existsSync(path.join(f.directory, "tree/b")), true);
    assert.match(h.operations.list().at(-1)!.message!, /无法自动回滚/);
    f.setBehavior({ denyRemoveNames: [], malformedEntries: ["../escape"] });
    await assert.rejects(() => h.operations.remove(id, "/tree"), /异常目录项/);
    assert.equal(existsSync(path.join(f.directory, "tree/b")), true);
    f.setBehavior({ malformedEntries: [], statDelayMs: 50 });
    const promise = h.operations.remove(id, "/tree");
    const op = h.operations.list().at(-1)!;
    h.operations.cancel(op.id);
    await assert.rejects(() => promise, /取消/);
    assert.equal(h.operations.list().at(-1)!.state, "cancelled");
  } finally {
    h.close();
    await f.close();
  }
});
test("SFTP permission edits preserve special bits, detect stale snapshots and reject links", async () => {
  const f = await fixture(),
    h = harness();
  try {
    const id = await ssh(h, f.port);
    chmodSync(path.join(f.directory, "hello.txt"), 0o4644);
    const p = await h.operations.permissions(id, "/hello.txt");
    assert.ok(p.supported);
    const result = await h.operations.chmod(id, "/hello.txt", 0o755, p.mode!);
    assert.equal(result.mode, 0o4755);
    assert.equal(
      statSync(path.join(f.directory, "hello.txt")).mode & 0o7777,
      0o4755,
    );
    await assert.rejects(
      () => h.operations.chmod(id, "/hello.txt", 0o644, p.mode!),
      /已改变/,
    );
    f.setBehavior({ chmod: false });
    await assert.rejects(
      () => h.operations.chmod(id, "/hello.txt", 0o644, result.mode!),
      /权限/,
    );
    if (process.platform !== "win32") {
      symlinkSync("hello.txt", path.join(f.directory, "link"));
      assert.equal(
        (await h.operations.permissions(id, "/link")).supported,
        false,
      );
      await assert.rejects(
        () => h.operations.chmod(id, "/link", 0o777, 0o755),
        /链接/,
      );
    }
  } finally {
    h.close();
    await f.close();
  }
});
test("exclusive first save creates empty/content files only on save and never overwrites a race", async () => {
  const f = await fixture(),
    h = harness();
  try {
    const id = await ssh(h, f.port),
      sftp = await h.sessions.sftp(id);
    assert.equal(existsSync(path.join(f.directory, "new.yml")), false);
    await createRemoteText(sftp, "/new.yml", "a: 1\n");
    assert.equal(
      readFileSync(path.join(f.directory, "new.yml"), "utf8"),
      "a: 1\n",
    );
    await assert.rejects(
      () => createRemoteText(sftp, "/new.yml", "replace"),
      /已存在/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "new.yml"), "utf8"),
      "a: 1\n",
    );
    await createRemoteText(sftp, "/empty", "");
    assert.equal(statSync(path.join(f.directory, "empty")).size, 0);
    f.setBehavior({ write: false });
    await assert.rejects(
      () => createRemoteText(sftp, "/denied", "x"),
      /创建失败/,
    );
    assert.equal(existsSync(path.join(f.directory, "denied")), false);
  } finally {
    h.close();
    await f.close();
  }
});
test("real FTP nested removal and supported/unsupported chmod are honest", async () => {
  for (const supported of [false, true]) {
    const f = await ftpFixture({ chmod: supported }),
      h = harness();
    try {
      const id = await h.sessions.connect({
        protocol: "ftp",
        host: "127.0.0.1",
        port: f.port,
        username: "test",
        password: "synthetic-ftp-password",
      });
      await waitFor(() => h.sessions.list()[0]?.status === "ready");
      mkdirSync(path.join(f.directory, "tree/sub"), { recursive: true });
      writeFileSync(path.join(f.directory, "tree/sub/a"), "a");
      await h.operations.remove(id, "/tree");
      assert.equal(existsSync(path.join(f.directory, "tree")), false);
      const mode = await h.operations.permissions(id, "/ftp.txt");
      assert.equal(mode.supported, supported);
      if (supported) {
        const after = await h.operations.chmod(
          id,
          "/ftp.txt",
          0o755,
          mode.mode!,
        );
        assert.equal(after.mode, 0o755);
      } else
        await assert.rejects(
          () => h.operations.chmod(id, "/ftp.txt", 0o755, 0o644),
          /不支持|未确认|未提供/,
        );
      await assert.rejects(
        () => h.operations.chmod(id, "/ftp.txt\r\nDELE x", 0o755, 0o644),
        /不安全/,
      );
    } finally {
      h.close();
      await f.close();
    }
  }
});

test("recursive deletion stops after confirmed progress on cancel/disconnect and preserves terminal on cancel", async () => {
  for (const mode of ["cancel", "disconnect"] as const) {
    const f = await fixture();
    let stopped = false;
    const h = harness({
      onEvent(e) {
        if (
          e.type === "file-operation" &&
          e.operation.state === "running" &&
          e.operation.count === 1 &&
          !stopped
        ) {
          stopped = true;
          if (mode === "cancel") h.operations.cancel(e.operation.id);
          else f.dropConnections();
        }
      },
    });
    try {
      const id = await ssh(h, f.port);
      await h.sessions.openTerminal(id);
      mkdirSync(path.join(f.directory, "tree"));
      for (const n of ["a", "b", "c"])
        writeFileSync(path.join(f.directory, "tree", n), n);
      await assert.rejects(
        () => h.operations.remove(id, "/tree"),
        /取消|连接|中断|失败/,
      );
      const op = h.operations.list().at(-1)!;
      assert.equal(op.count, 1);
      assert.equal(op.state, mode === "cancel" ? "cancelled" : "error");
      assert.match(op.message!, /无法自动回滚/);
      assert.equal(
        ["a", "b", "c"].filter((n) =>
          existsSync(path.join(f.directory, "tree", n)),
        ).length,
        2,
      );
      if (mode === "cancel") {
        await h.sessions.listFiles(id, "/");
        h.sessions.input(id, "after-delete-cancel");
        await waitFor(() => f.received.includes("after-delete-cancel"));
        assert.equal(f.authentications, 1);
      }
    } finally {
      h.close();
      await f.close();
    }
  }
});
test("active transfer guard and bounded preflight reject without removing any file", async () => {
  const f = await fixture();
  let blocked = true;
  const h = harness({ blocked: () => blocked });
  try {
    const id = await ssh(h, f.port);
    await assert.rejects(() => h.operations.remove(id, "/hello.txt"), /传输/);
    await assert.rejects(
      () => h.operations.chmod(id, "/hello.txt", 0o755, 0o644),
      /传输/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      "hello 文件\n",
    );
    blocked = false;
    mkdirSync(path.join(f.directory, "many"));
    for (let i = 0; i < 2001; i++)
      writeFileSync(path.join(f.directory, "many", String(i)), "");
    await assert.rejects(() => h.operations.remove(id, "/many"), /2000/);
    assert.equal(h.operations.list().at(-1)?.count, 0);
    assert.equal(existsSync(path.join(f.directory, "many/0")), true);
    let current = path.join(f.directory, "deep");
    mkdirSync(current);
    for (let i = 0; i < 33; i++) {
      current = path.join(current, "d");
      mkdirSync(current);
    }
    await assert.rejects(() => h.operations.remove(id, "/deep"), /32/);
    assert.equal(h.operations.list().at(-1)?.count, 0);
    assert.equal(existsSync(current), true);
    mkdirSync(path.join(f.directory, "empty"));
    await h.operations.remove(id, "/empty");
    assert.equal(h.operations.list().at(-1)?.count, 1);
  } finally {
    h.close();
    await f.close();
  }
});
test("FTP rejects ambiguous/link traversal before mutations and reports denied partial deletion", async () => {
  for (const kind of ["link", "unknown", "denied"] as const) {
    const f = await ftpFixture({
        unknownTypes: kind === "unknown",
        denyRemoveNames: kind === "denied" ? ["b"] : [],
      }),
      h = harness();
    try {
      const id = await h.sessions.connect({
        protocol: "ftp",
        host: "127.0.0.1",
        port: f.port,
        username: "test",
        password: "synthetic-ftp-password",
      });
      await waitFor(() => h.sessions.list()[0]?.status === "ready");
      mkdirSync(path.join(f.directory, "tree"));
      writeFileSync(path.join(f.directory, "tree/a"), "a");
      writeFileSync(path.join(f.directory, "tree/b"), "b");
      const outside = path.join(h.dir, "outside");
      writeFileSync(outside, "keep");
      if (kind === "link" && process.platform !== "win32")
        symlinkSync(outside, path.join(f.directory, "tree/link"));
      if (kind === "link" && process.platform === "win32") continue;
      await assert.rejects(
        () => h.operations.remove(id, "/tree"),
        kind === "link" ? /链接/ : kind === "unknown" ? /类型/ : /权限/,
      );
      assert.equal(
        h.operations.list().at(-1)?.count,
        kind === "denied" ? 1 : 0,
      );
      assert.equal(readFileSync(outside, "utf8"), "keep");
      assert.equal(existsSync(path.join(f.directory, "tree/b")), true);
    } finally {
      h.close();
      await f.close();
    }
  }
});
