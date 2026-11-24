import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { Store } from "../src/main/store";
test("stored secrets are encrypted, exports omit them, unknown keys are stripped", async () => {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-store-"));
  try {
    const s = new Store(dir, {
      encrypt: async (p) => Buffer.from(p).toString("base64"),
      decrypt: async (p) => Buffer.from(p, "base64").toString(),
    });
    const [p] = await s.save({
      protocol: "ssh",
      host: "localhost",
      port: 22,
      username: "u",
      group: "test",
      password: "private-value",
      access_token: "never-save",
    });
    const content = readFileSync(path.join(dir, "vault.json"), "utf8");
    assert.ok(!content.includes("private-value"));
    assert.ok(!content.includes("never-save"));
    assert.equal((await s.resolve(p.id)).password, "private-value");
    assert.ok(!JSON.stringify(s.export()).includes("encryptedPassword"));
    assert.ok(!JSON.stringify(s.profiles()).includes("private-value"));
    await s.save({ ...p, title: "changed" });
    assert.equal((await s.resolve(p.id)).password, "private-value");
    const p2 = (
      await s.save({ ...p, id: undefined, host: "other", jumpId: p.id })
    )[1];
    await assert.rejects(() => s.save({ ...p, jumpId: p2.id }), /循环/);
    assert.throws(() => s.delete(p.id), /跳板机/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
