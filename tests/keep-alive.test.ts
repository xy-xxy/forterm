import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync, rmSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { SftpHeartbeat } from "../src/main/sftp-heartbeat";
import { Store } from "../src/main/store";
import { DEFAULT_KEEP_ALIVE } from "../src/shared/keep-alive";

test("keepalive settings migrate, validate, persist and roll back failed writes", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-heartbeat-"));
  const cipher = {
    encrypt: async (s: string) => s,
    decrypt: async (s: string) => s,
  };
  try {
    writeFileSync(
      path.join(dir, "vault.json"),
      JSON.stringify({ profiles: [], hosts: {} }),
    );
    const store = new Store(dir, cipher);
    assert.deepEqual(store.keepAlive(), DEFAULT_KEEP_ALIVE);
    const settings = { enabled: false, intervalSeconds: 60, sftpProbe: false };
    store.setKeepAlive(settings);
    assert.deepEqual(new Store(dir, cipher).keepAlive(), settings);
    for (const bad of [4, 601, 5.5, NaN, "15"]) {
      assert.throws(() =>
        store.setKeepAlive({ ...settings, intervalSeconds: bad }),
      );
      assert.deepEqual(store.keepAlive(), settings);
    }
    assert.throws(() => store.setKeepAlive({ ...settings, enabled: "false" }));
    const copy = store.keepAlive();
    copy.enabled = true;
    assert.equal(store.keepAlive().enabled, false);
    mkdirSync(path.join(dir, "vault.json.tmp"));
    assert.throws(() => store.setKeepAlive({ ...settings, enabled: true }));
    assert.deepEqual(store.keepAlive(), settings);
    writeFileSync(
      path.join(dir, "vault.json"),
      JSON.stringify({
        profiles: [],
        hosts: {},
        keepAlive: { intervalSeconds: 0 },
      }),
    );
    assert.deepEqual(new Store(dir, cipher).keepAlive(), DEFAULT_KEEP_ALIVE);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("SFTP scheduler bounds outstanding probes, stops after timeout and cleans timers", async (t) => {
  t.mock.timers.enable({ apis: ["setInterval", "setTimeout"] });
  let count = 0;
  let finish!: () => void;
  const heartbeat = new SftpHeartbeat(
    100,
    () => {
      count++;
      return new Promise<void>((resolve) => {
        finish = resolve;
      });
    },
    350,
  );
  t.mock.timers.tick(100);
  await Promise.resolve();
  assert.equal(count, 1);
  t.mock.timers.tick(300);
  await Promise.resolve();
  assert.equal(count, 1, "stalled probe must not stack requests");
  t.mock.timers.tick(100);
  finish();
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
  t.mock.timers.tick(1000);
  await Promise.resolve();
  assert.equal(
    count,
    1,
    "timeout must stop future scheduling even after late response",
  );
  heartbeat.stop();
  heartbeat.stop();
  let stoppedCount = 0;
  const stopped = new SftpHeartbeat(100, async () => {
    stoppedCount++;
  });
  stopped.stop();
  t.mock.timers.tick(1000);
  await Promise.resolve();
  assert.equal(stoppedCount, 0);
});

test("SFTP error responses allow the next scheduled probe without immediate retry", async (t) => {
  t.mock.timers.enable({ apis: ["setInterval", "setTimeout"] });
  let count = 0;
  const heartbeat = new SftpHeartbeat(100, async () => {
    count++;
    throw new Error("permission denied");
  });
  for (let i = 1; i <= 2; i++) {
    t.mock.timers.tick(100);
    for (let j = 0; j < 6; j++) await Promise.resolve();
    assert.equal(count, i);
  }
  heartbeat.stop();
  t.mock.timers.tick(1000);
  await Promise.resolve();
  assert.equal(count, 2);
});
