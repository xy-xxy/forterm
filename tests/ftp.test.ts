import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { Store } from "../src/main/store";
import { Sessions } from "../src/main/sessions";
import { Transfers } from "../src/main/transfers";
import { Prompts } from "../src/main/prompts";
import { ftpFixture } from "./ftp-fixture";
import { waitFor } from "./fixture";
import { parseLaunch, parseURL } from "../src/main/launch";
test("real FTP list, upload/download and file operations on an independent connection", async () => {
  const f = await ftpFixture(),
    dir = mkdtempSync(path.join(tmpdir(), "forterm-ftp-test-"));
  const store = new Store(dir, {
    encrypt: async (s) => s,
    decrypt: async (s) => s,
  });
  const prompts = new Prompts(() => {}),
    sessions = new Sessions(() => {}, store, prompts),
    transfers = new Transfers(sessions, () => {});
  try {
    const id = await sessions.connect({
      protocol: "ftp",
      host: "127.0.0.1",
      port: f.port,
      username: "test",
      password: "synthetic-ftp-password",
    });
    await waitFor(() => sessions.list()[0]?.status === "ready");
    assert.equal((await sessions.listFiles(id, ".")).files[0].name, "ftp.txt");
    const source = path.join(dir, "source.bin"),
      target = path.join(dir, "download.bin");
    const bytes = Buffer.alloc(120000, 41);
    writeFileSync(source, bytes);
    await transfers.start(id, source, "/upload.bin", "upload");
    await waitFor(() => transfers.list().every((j) => j.state !== "running"));
    assert.equal(transfers.list()[0].state, "done");
    assert.deepEqual(readFileSync(path.join(f.directory, "upload.bin")), bytes);
    await transfers.start(id, target, "/upload.bin", "download");
    await waitFor(() => transfers.list().every((j) => j.state !== "running"));
    assert.equal(transfers.list()[1].state, "done");
    assert.deepEqual(readFileSync(target), bytes);
    await sessions.fileAction(id, "mkdir", "/dir");
    await sessions.fileAction(id, "rename", "/dir", "/renamed");
    await sessions.fileAction(id, "rmdir", "/renamed");
    await assert.rejects(() => sessions.sftp(id), /FTP 不是 SFTP/);
    assert.equal(f.authentications, 1);
    assert.equal(f.connections, 1);
    assert.ok(f.verbs.includes("STOR") && f.verbs.includes("RETR"));
  } finally {
    transfers.close();
    sessions.closeAll();
    prompts.close();
    await f.close();
    rmSync(dir, { recursive: true, force: true });
  }
});
test("FTP URLs remain FTP through Xftp, default port and redacted diagnostics", () => {
  const url = "ftp://synthetic:private-value@127.0.0.1";
  assert.equal(parseURL(url).port, 21);
  assert.equal(parseLaunch(["--entry=xftp", url]).connection?.protocol, "ftp");
  assert.ok(
    !JSON.stringify(parseLaunch(["--diagnose", url]).report).includes(
      "private-value",
    ),
  );
});
