import { test } from "node:test";
import assert from "node:assert/strict";
import {
  mkdtempSync,
  rmSync,
  writeFileSync,
  readFileSync,
  readdirSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { createHash } from "node:crypto";
import { Store } from "../src/main/store";
import { Prompts } from "../src/main/prompts";
import { Sessions } from "../src/main/sessions";
import { Transfers } from "../src/main/transfers";
import { fixture, waitFor } from "./fixture";
import { ftpFixture } from "./ftp-fixture";
const active = (state: string) => state === "queued" || state === "running";
function harness(stall = 5000) {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-queue-")),
    store = new Store(dir, {
      encrypt: async (s) => s,
      decrypt: async (s) => s,
    });
  const prompts = new Prompts((e) => {
    if (e.type === "prompt")
      queueMicrotask(() => prompts.answer(e.prompt.id, []));
  });
  const sessions = new Sessions(() => {}, store, prompts, {
    fileTimeoutMs: 200,
  });
  const transfers = new Transfers(sessions, () => {}, stall);
  return {
    dir,
    store,
    sessions,
    transfers,
    close() {
      transfers.close();
      sessions.closeAll();
      prompts.close();
      rmSync(dir, { recursive: true, force: true });
    },
  };
}
const digest = (b: Buffer) => createHash("sha256").update(b).digest("hex");
test("20-file batch plus append stays serial under a one-open-file server, metadata interleaves without denial", async () => {
  const f = await fixture({
      maxOpenFiles: 1,
      writeDelayMs: 15,
      denyRealpathWhileOpen: true,
    }),
    h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
      temporary: true,
    });
    await waitFor(() => h.sessions.list()[0]?.terminalStatus === "ready");
    const values = new Map<string, Buffer>();
    for (let i = 0; i < 24; i++) {
      const b = Buffer.alloc(48000, i + 1),
        file = path.join(h.dir, `upload-${i}.bin`);
      writeFileSync(file, b);
      values.set(`upload-${i}.bin`, b);
      await h.transfers.start(id, file, `/upload-${i}.bin`, "upload");
      if (i === 19) {
        assert.ok(h.transfers.list().some((t) => t.state === "queued"));
        await h.sessions.listFiles(id, ".");
      }
    }
    await waitFor(
      () => h.transfers.list().every((t) => !active(t.state)),
      15000,
    );
    assert.ok(h.transfers.list().every((t) => t.state === "done"));
    assert.equal(f.maxOpenFiles, 1);
    assert.equal(f.deniedOpen, 0);
    assert.equal(f.deniedRealpath, 0);
    assert.equal(f.openFiles, 0);
    for (const [name, data] of values)
      assert.equal(
        digest(readFileSync(path.join(f.directory, name))),
        digest(data),
      );
    h.sessions.input(id, "terminal after batch");
    await waitFor(() => f.received.includes("terminal after batch"));
    assert.equal(f.authentications, 1);
  } finally {
    h.close();
    await f.close();
  }
});
test("stalled write times out, stops pending files, preserves SSH and can recover browsing on a new SFTP channel", async () => {
  const f = await fixture({
      maxOpenFiles: 1,
      stallWriteNames: ["stalled.bin"],
      denyRealpathWhileOpen: true,
    }),
    h = harness(180);
  try {
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
      temporary: true,
    });
    await waitFor(() => h.sessions.list()[0]?.terminalStatus === "ready");
    const file = path.join(h.dir, "source.bin");
    writeFileSync(file, Buffer.alloc(100000, 72));
    await h.transfers.start(id, file, "/stalled.bin", "upload");
    for (let i = 0; i < 20; i++)
      await h.transfers.start(id, file, `/not-started-${i}.bin`, "upload");
    await waitFor(
      () => h.transfers.list().every((t) => !active(t.state)),
      5000,
    );
    assert.equal(h.transfers.list()[0].state, "error");
    assert.match(h.transfers.list()[0].message!, /停滞/);
    assert.equal(
      readdirSync(f.directory).filter((n) => n.startsWith("not-started"))
        .length,
      0,
    );
    await waitFor(() => f.openFiles === 0);
    f.setBehavior({ stallWriteNames: [] });
    const listing = await h.sessions.listFiles(id, ".");
    assert.ok(listing.files.length);
    await h.transfers.start(id, file, "/recovered.bin", "upload");
    await waitFor(() => h.transfers.list().at(-1)?.state === "done");
    h.sessions.input(id, "still terminal");
    await waitFor(() => f.received.includes("still terminal"));
    assert.equal(f.authentications, 1);
    assert.ok(f.sftpRequests >= 2);
  } finally {
    h.close();
    await f.close();
  }
});
test("queued cancel is immediate, active cancel exits and file lanes do not block another connection", async () => {
  const slow = await fixture({
      stallWriteNames: ["slow.bin"],
      maxOpenFiles: 1,
    }),
    fast = await fixture({ maxOpenFiles: 1 }),
    h = harness(5000);
  try {
    const connect = async (port: number) => {
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
    };
    const one = await connect(slow.port),
      two = await connect(fast.port),
      file = path.join(h.dir, "source");
    writeFileSync(file, Buffer.alloc(50000, 3));
    await h.transfers.start(one, file, "/slow.bin", "upload");
    const queued = await h.transfers.start(one, file, "/queued.bin", "upload");
    h.transfers.cancel(queued);
    assert.equal(
      h.transfers.list().find((t) => t.id === queued)?.state,
      "cancelled",
    );
    await h.transfers.start(two, file, "/fast.bin", "upload");
    await waitFor(
      () =>
        h.transfers.list().find((t) => t.sessionId === two)?.state === "done",
    );
    const running = h.transfers
      .list()
      .find((t) => t.sessionId === one && t.state === "running")!;
    h.transfers.cancel(running.id);
    await waitFor(() => !h.transfers.list().some((t) => active(t.state)));
    await waitFor(() => slow.openFiles === 0);
    assert.equal(readdirSync(slow.directory).includes("queued.bin"), false);
  } finally {
    h.close();
    await slow.close();
    await fast.close();
  }
});
test("metadata denial, timeout and cancellation release the lane without reauthenticating", async () => {
  const f = await fixture(),
    h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.terminalStatus === "ready");
    await h.sessions.listFiles(id, ".");
    f.setBehavior({ denyRealpath: true });
    await assert.rejects(
      () => h.sessions.listFiles(id, "."),
      /目录路径解析失败/,
    );
    f.setBehavior({ denyRealpath: false, ignoreRealpath: true });
    await assert.rejects(() => h.sessions.listFiles(id, "."), /超时/);
    f.setBehavior({ ignoreRealpath: false });
    await h.sessions.listFiles(id, ".");
    f.setBehavior({ ignoreRealpath: true });
    const pending = h.sessions.listFiles(id, ".", "cancel-metadata");
    setTimeout(() => h.sessions.cancelFileRequest(id, "cancel-metadata"), 30);
    await assert.rejects(() => pending, /取消/);
    f.setBehavior({ ignoreRealpath: false });
    await h.sessions.listFiles(id, ".");
    assert.equal(f.authentications, 1);
    h.sessions.input(id, "after metadata failures");
    await waitFor(() => f.received.includes("after metadata"));
  } finally {
    h.close();
    await f.close();
  }
});
test("real FTP batches also serialize and queued cancellations do not open streams", async () => {
  const f = await ftpFixture(),
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
    const file = path.join(h.dir, "source");
    writeFileSync(file, Buffer.alloc(10000, 4));
    for (let i = 0; i < 20; i++)
      await h.transfers.start(id, file, `/file-${i}.bin`, "upload");
    await waitFor(
      () => h.transfers.list().every((t) => !active(t.state)),
      10000,
    );
    assert.ok(h.transfers.list().every((t) => t.state === "done"));
    for (let i = 0; i < 20; i++)
      assert.equal(
        digest(readFileSync(path.join(f.directory, `file-${i}.bin`))),
        digest(readFileSync(file)),
      );
    assert.equal(f.connections, 1);
  } finally {
    h.close();
    await f.close();
  }
});

test("SFTP heartbeat skips the busy transfer lane while SSH keepalive continues", async () => {
  const f = await fixture({
      stallWriteNames: ["heartbeat-stall.bin"],
      denyRealpathWhileOpen: true,
    }),
    h = harness(12000);
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
    await waitFor(() => h.sessions.list()[0]?.terminalStatus === "ready");
    await h.sessions.listFiles(id, ".");
    const initial = f.realpathRequests;
    const file = path.join(h.dir, "source");
    writeFileSync(file, Buffer.alloc(100000, 9));
    await h.transfers.start(id, file, "/heartbeat-stall.bin", "upload");
    await waitFor(() => f.openFiles === 1);
    await new Promise((r) => setTimeout(r, 5300));
    assert.equal(f.realpathRequests, initial);
    assert.equal(f.deniedRealpath, 0);
    assert.ok(f.keepaliveRequests > 0);
    h.transfers.cancel(h.transfers.list()[0].id);
    await waitFor(() => !h.transfers.list().some((t) => active(t.state)));
    await waitFor(() => f.openFiles === 0);
  } finally {
    h.close();
    await f.close();
  }
});
test("slow stat cannot leave downloads permanently running or corrupt their original target", async () => {
  const f = await fixture({ statDelayMs: 500 }),
    h = harness();
  try {
    const id = await h.sessions.connect({
      protocol: "sftp",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
    });
    await waitFor(() => h.sessions.list()[0]?.status === "ready");
    const target = path.join(h.dir, "target");
    writeFileSync(target, "keep-original");
    await h.transfers.start(id, target, "/hello.txt", "download");
    await waitFor(() => h.transfers.list()[0]?.state === "error");
    assert.equal(readFileSync(target, "utf8"), "keep-original");
    assert.equal(f.openFiles, 0);
    assert.ok(!readdirSync(h.dir).some((n) => n.endsWith(".part")));
    f.setBehavior({ statDelayMs: 0 });
    await h.sessions.listFiles(id, ".");
  } finally {
    h.close();
    await f.close();
  }
});
