import { test } from "node:test";
import assert from "node:assert/strict";
import {
  mkdtempSync,
  rmSync,
  readFileSync,
  writeFileSync,
  readdirSync,
  symlinkSync,
  chmodSync,
  statSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { randomBytes } from "node:crypto";
import { Sessions } from "../src/main/sessions";
import { Transfers } from "../src/main/transfers";
import { Prompts } from "../src/main/prompts";
import { Store } from "../src/main/store";
import type { AppEvent } from "../src/shared/types";
import {
  readRemoteText,
  saveRemoteText,
  TEXT_LIMIT,
} from "../src/main/remote-text";
import { fixture, waitFor } from "./fixture";
function harness(accept = true) {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-test-"));
  const events: AppEvent[] = [];
  const store = new Store(dir, {
    encrypt: async (s) => s,
    decrypt: async (s) => s,
  });
  const prompts = new Prompts((e) => {
    events.push(e);
    if (e.type === "prompt")
      queueMicrotask(() =>
        prompts.answer(
          e.prompt.id,
          accept ? (e.prompt.kind === "host" ? [] : ["123456"]) : null,
        ),
      );
  });
  const sessions = new Sessions((e) => events.push(e), store, prompts);
  return {
    dir,
    events,
    store,
    prompts,
    sessions,
    close() {
      prompts.close();
      sessions.closeAll();
      rmSync(dir, { recursive: true, force: true });
    },
  };
}
test("real SSH auth, split UTF-8 data, PTY input and on-demand SFTP + transfers", async () => {
  const f = await fixture();
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
      temporary: true,
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    assert.equal(f.sftpRequests, 0);
    await waitFor(() => h.sessions.attach(id).includes("你好"));
    h.sessions.input(id, "echo test\n");
    await waitFor(() => f.received.includes("echo test"));
    h.sessions.resize(id, 120, 40);
    const listing = await h.sessions.listFiles(id, ".");
    assert.equal(listing.files[0].name, "hello.txt");
    assert.equal(f.sftpRequests, 1);
    await h.sessions.fileAction(id, "mkdir", "/new");
    await h.sessions.fileAction(id, "rename", "/new", "/renamed");
    await h.sessions.fileAction(id, "rmdir", "/renamed");
    const transfers = new Transfers(h.sessions, (e) => h.events.push(e));
    const source = path.join(h.dir, "source.bin");
    const target = path.join(h.dir, "download.bin");
    const data = randomBytes(200000);
    writeFileSync(source, data);
    await transfers.start(id, source, "/upload.bin", "upload");
    await waitFor(() => transfers.list().every((t) => t.state !== "running"));
    assert.equal(transfers.list()[0].state, "done");
    assert.deepEqual(readFileSync(path.join(f.directory, "upload.bin")), data);
    await transfers.start(id, target, "/upload.bin", "download");
    await waitFor(() => transfers.list().every((t) => t.state !== "running"));
    assert.equal(transfers.list()[1].state, "done");
    assert.deepEqual(readFileSync(target), data);
    assert.match(await h.sessions.monitor(id), /MEMORY/);
    assert.ok(!JSON.stringify(h.sessions.list()).includes("one-time"));
  } finally {
    h.close();
    await f.close();
  }
});
test("SFTP denial leaves the SSH terminal usable", async () => {
  const f = await fixture({ sftp: false });
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0].status === "ready");
    await assert.rejects(() => h.sessions.listFiles(id, "."), /SFTP/);
    assert.equal(h.sessions.list()[0].status, "ready");
    h.sessions.input(id, "still alive");
    await waitFor(() => f.received.includes("still alive"));
  } finally {
    h.close();
    await f.close();
  }
});
test("keyboard-interactive authentication is forwarded as a secret prompt", async () => {
  const f = await fixture({ interactive: true });
  const h = harness();
  try {
    await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "ignored",
    });
    await waitFor(() => h.sessions.list()[0].status === "ready");
    assert.ok(
      h.events.some(
        (e) =>
          e.type === "prompt" &&
          e.prompt.kind === "auth" &&
          e.prompt.fields[0].secret,
      ),
    );
  } finally {
    h.close();
    await f.close();
  }
});
test("rejecting a new host fingerprint prevents opening a shell", async () => {
  const f = await fixture();
  const h = harness(false);
  try {
    await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0].status === "error");
    assert.equal(f.received, "");
    assert.equal(h.store.host("127.0.0.1", f.port), undefined);
  } finally {
    h.close();
    await f.close();
  }
});
test("jump connection forwards the SSH transport through the configured hop", async () => {
  const jump = await fixture(),
    target = await fixture();
  const h = harness();
  try {
    const [p] = await h.store.save({
      protocol: "ssh",
      host: "127.0.0.1",
      port: jump.port,
      username: "ticket",
      password: "one-time@password",
      group: "test",
    });
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: target.port,
      username: "ticket",
      password: "one-time@password",
      jumpId: p.id,
    });
    await waitFor(() => h.sessions.list()[0].status === "ready");
    h.sessions.input(id, "via jump");
    await waitFor(() => target.received.includes("via jump"));
    assert.equal(jump.received, "");
  } finally {
    h.close();
    await jump.close();
    await target.close();
  }
});

test("real SSH keepalive uses no shell input, probes only open SFTP and preserves session snapshots", async () => {
  const f = await fixture();
  const h = harness();
  try {
    h.store.setKeepAlive({
      enabled: true,
      intervalSeconds: 5,
      sftpProbe: true,
    });
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    h.store.setKeepAlive({
      enabled: false,
      intervalSeconds: 60,
      sftpProbe: false,
    });
    assert.deepEqual(h.sessions.list()[0].keepAlive, {
      enabled: true,
      intervalSeconds: 5,
      sftpProbe: true,
    });
    await waitFor(() => f.keepaliveRequests > 0, 7500);
    assert.equal(f.sftpRequests, 0, "keepalive must not open SFTP on its own");
    assert.equal(f.received, "", "no synthetic terminal input");
    await h.sessions.listFiles(id, ".");
    const initial = f.realpathRequests;
    await waitFor(() => f.realpathRequests > initial, 7500);
    assert.equal(f.sftpRequests, 1, "probe must reuse the channel");
    assert.equal(f.received, "");
    h.sessions.close(id);
    const afterClose = f.realpathRequests;
    await new Promise((r) => setTimeout(r, 5200));
    assert.equal(f.realpathRequests, afterClose);
    assert.equal(h.sessions.list().length, 0);
  } finally {
    h.close();
    await f.close();
  }
});

test("Xftp session opens shell, files, editor and monitor on one authenticated transport", async () => {
  const f = await fixture();
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
      temporary: true,
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    await Promise.all([
      h.sessions.openTerminal(id),
      h.sessions.openTerminal(id),
    ]);
    const ftp = await h.sessions.sftp(id);
    const [doc, listing, monitor] = await Promise.all([
      readRemoteText(ftp, "/hello.txt"),
      h.sessions.listFiles(id, "."),
      h.sessions.monitor(id),
    ]);
    assert.equal(doc.text, "hello 文件\n");
    assert.equal(listing.files.length, 1);
    assert.match(monitor, /LOAD/);
    h.sessions.input(id, "parallel terminal");
    const saved = await saveRemoteText(
      ftp,
      "/hello.txt",
      "更新\r\n",
      doc.revision,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      "更新\r\n",
    );
    assert.equal(saved.text, "更新\r\n");
    await waitFor(() => f.received.includes("parallel terminal"));
    assert.equal(f.shellRequests, 1);
    assert.equal(f.sftpRequests, 1);
    assert.equal(f.authentications, 1);
    assert.equal(h.sessions.list()[0].terminalStatus, "ready");
  } finally {
    h.close();
    await f.close();
  }
});

test("editor refuses oversized, binary, non-UTF8 and conflicting files without overwriting", async () => {
  const f = await fixture();
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    const ftp = await h.sessions.sftp(id);
    const doc = await readRemoteText(ftp, "/hello.txt");
    writeFileSync(path.join(f.directory, "hello.txt"), "server changed!");
    await assert.rejects(
      () => saveRemoteText(ftp, "/hello.txt", "my draft", doc.revision),
      /冲突/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      "server changed!",
    );
    for (const [name, data, message] of [
      ["large", Buffer.alloc(TEXT_LIMIT + 1, 65), /1 MiB/],
      ["binary", Buffer.from([65, 0, 66]), /二进制/],
      ["encoding", Buffer.from([0xff, 0xfe]), /UTF-8/],
    ] as const) {
      writeFileSync(path.join(f.directory, name), data);
      await assert.rejects(() => readRemoteText(ftp, "/" + name), message);
    }
    await assert.rejects(
      () =>
        saveRemoteText(
          ftp,
          "/hello.txt",
          "x".repeat(TEXT_LIMIT + 1),
          doc.revision,
        ),
      /1 MiB/,
    );
    h.sessions.input(id, "");
  } finally {
    h.close();
    await f.close();
  }
});

test("shell and monitor denial preserve SFTP editing, and write denial preserves original", async () => {
  const f = await fixture({ shell: false, exec: false, write: false });
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    await assert.rejects(() => h.sessions.openTerminal(id), /终端/);
    await assert.rejects(() => h.sessions.monitor(id), /监控/);
    const ftp = await h.sessions.sftp(id);
    const doc = await readRemoteText(ftp, "/hello.txt");
    await assert.rejects(
      () => saveRemoteText(ftp, "/hello.txt", "changed", doc.revision),
      /保存失败/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      doc.text,
    );
    assert.equal((await h.sessions.listFiles(id, ".")).files.length, 1);
    assert.equal(h.sessions.list()[0].status, "ready");
    assert.equal(f.authentications, 1);
  } finally {
    h.close();
    await f.close();
  }
});

test("Xftp SFTP denial still allows opening terminal and monitoring without reauthentication", async () => {
  const f = await fixture({ sftp: false });
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    await h.sessions.openTerminal(id);
    await assert.rejects(() => h.sessions.listFiles(id, "."), /SFTP/);
    assert.match(await h.sessions.monitor(id), /MEMORY/);
    h.sessions.input(id, "files denied but terminal alive");
    await waitFor(() => f.received.includes("terminal alive"));
    assert.equal(f.authentications, 1);
    f.dropConnections();
    await waitFor(() => h.sessions.list()[0].status === "closed");
    await assert.rejects(() => h.sessions.openTerminal(id), /关闭/);
  } finally {
    h.close();
    await f.close();
  }
});

test("editor preserves BOM and CRLF, rejects symlinks and detects changes during save", async () => {
  let mutate = () => {};
  const f = await fixture({ onWrite: () => mutate() });
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    const ftp = await h.sessions.sftp(id);
    writeFileSync(
      path.join(f.directory, "hello.txt"),
      "\ufeffline1\r\nline2\r\n",
    );
    chmodSync(path.join(f.directory, "hello.txt"), 0o664);
    const doc = await readRemoteText(ftp, "/hello.txt");
    assert.equal(doc.text, "\ufeffline1\r\nline2\r\n");
    const saved = await saveRemoteText(
      ftp,
      "/hello.txt",
      "\ufeffchanged\r\n",
      doc.revision,
    );
    assert.deepEqual(
      readFileSync(path.join(f.directory, "hello.txt")),
      Buffer.from("\ufeffchanged\r\n"),
    );
    if (process.platform !== "win32") {
      assert.equal(
        statSync(path.join(f.directory, "hello.txt")).mode & 0o777,
        0o664,
        "save must restore group-write bit despite creation umask",
      );
      symlinkSync("hello.txt", path.join(f.directory, "link.txt"));
      await assert.rejects(() => readRemoteText(ftp, "/link.txt"), /普通文件/);
    }
    mutate = () => {
      writeFileSync(path.join(f.directory, "hello.txt"), "other writer");
    };
    await assert.rejects(
      () => saveRemoteText(ftp, "/hello.txt", "second save", saved.revision),
      /冲突/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      "other writer",
    );
    await waitFor(
      () =>
        !readdirSync(f.directory).some((n) => n.startsWith(".forterm-edit-")),
    );
  } finally {
    h.close();
    await f.close();
  }
});

test("failed replacement retains original and removes staged editor data", async () => {
  const f = await fixture({ rename: false });
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    const ftp = await h.sessions.sftp(id);
    const doc = await readRemoteText(ftp, "/hello.txt");
    await assert.rejects(
      () => saveRemoteText(ftp, "/hello.txt", "new contents", doc.revision),
      /替换失败/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      doc.text,
    );
    await waitFor(() => readdirSync(f.directory).length === 1);
  } finally {
    h.close();
    await f.close();
  }
});

test("download cancellation removes partial data and completed queue clearing preserves active work", async () => {
  const f = await fixture();
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    writeFileSync(
      path.join(f.directory, "large.bin"),
      Buffer.alloc(8 * 1024 * 1024, 42),
    );
    const target = path.join(h.dir, "target.bin");
    writeFileSync(target, "original target");
    const transfers = new Transfers(h.sessions, (e) => h.events.push(e));
    await transfers.start(id, target, "/large.bin", "download");
    const job = transfers.list()[0];
    transfers.clearCompleted();
    assert.equal(transfers.list().length, 1);
    transfers.cancel(job.id);
    await waitFor(() => transfers.list()[0].state === "cancelled");
    assert.equal(readFileSync(target, "utf8"), "original target");
    assert.equal(
      readdirSync(h.dir).filter((name) => name.endsWith(".part")).length,
      0,
    );
    transfers.clearCompleted();
    assert.equal(transfers.list().length, 0);
    assert.ok(h.events.some((e) => e.type === "transfers-cleared"));
  } finally {
    h.close();
    await f.close();
  }
});

test("disabled SSH keepalive or disabled SFTP probing produces no background SFTP traffic", async () => {
  await Promise.all(
    [false, true].map(async (enabled) => {
      const f = await fixture();
      const h = harness();
      try {
        h.store.setKeepAlive({ enabled, intervalSeconds: 5, sftpProbe: false });
        const id = await h.sessions.connect({
          protocol: "sftp",
          host: "127.0.0.1",
          port: f.port,
          username: "ticket",
          password: "one-time@password",
        });
        await waitFor(() => h.sessions.list()[0]?.status === "ready");
        await h.sessions.listFiles(id, ".");
        const count = f.realpathRequests;
        await new Promise((resolve) => setTimeout(resolve, 5200));
        assert.equal(f.realpathRequests, count);
        assert.equal(f.sftpRequests, 1);
        assert.equal(f.received, "");
        if (!enabled) assert.equal(f.keepaliveRequests, 0);
      } finally {
        h.close();
        await f.close();
      }
    }),
  );
});

test("editor chmod refusal leaves the original file and its permissions intact", async () => {
  const f = await fixture({ chmod: false });
  const h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    const ftp = await h.sessions.sftp(id);
    const doc = await readRemoteText(ftp, "/hello.txt");
    const before = statSync(path.join(f.directory, "hello.txt")).mode;
    await assert.rejects(
      () => saveRemoteText(ftp, "/hello.txt", "new", doc.revision),
      /权限/,
    );
    assert.equal(
      readFileSync(path.join(f.directory, "hello.txt"), "utf8"),
      doc.text,
    );
    assert.equal(statSync(path.join(f.directory, "hello.txt")).mode, before);
    await waitFor(() => readdirSync(f.directory).length === 1);
  } finally {
    h.close();
    await f.close();
  }
});
