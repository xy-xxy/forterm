import { test } from "node:test";
import assert from "node:assert/strict";
import {
  mkdtempSync,
  readFileSync,
  writeFileSync,
  rmSync,
  mkdirSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { createCipheriv, createDecipheriv, randomBytes } from "node:crypto";
import { Store, type Cipher } from "../src/main/store";
import { DEFAULT_TOTP } from "../src/shared/bastion";
const seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
  password = "synthetic-portal-password";
function cipher(): Cipher {
  const key = randomBytes(32);
  return {
    encrypt: async (text) => {
      const nonce = randomBytes(12),
        c = createCipheriv("aes-256-gcm", key, nonce);
      return Buffer.concat([
        nonce,
        c.update(text),
        c.final(),
        c.getAuthTag(),
      ]).toString("base64");
    },
    decrypt: async (value) => {
      const b = Buffer.from(value, "base64"),
        d = createDecipheriv("aes-256-gcm", key, b.subarray(0, 12));
      d.setAuthTag(b.subarray(-16));
      return Buffer.concat([
        d.update(b.subarray(12, -16)),
        d.final(),
      ]).toString();
    },
  };
}
const input = () => ({
  name: "合成堡垒机",
  portalUrl: "https://bastion.example/",
  username: "test-user",
  passwordAction: "replace" as const,
  password,
  seedAction: "replace" as const,
  seedInput: seed,
  totpEnabled: true,
  totp: { ...DEFAULT_TOTP },
});
test("old vault migration, encrypted account secrets, no secret echo/export and metadata-only import", async () => {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-account-"));
  try {
    writeFileSync(
      path.join(dir, "vault.json"),
      JSON.stringify({
        profiles: [],
        hosts: {},
        keepAlive: { enabled: true, intervalSeconds: 15, sftpProbe: true },
      }),
    );
    const c = cipher(),
      store = new Store(dir, c);
    assert.deepEqual(store.bastionAccounts(), []);
    const [account] = await store.saveBastionAccount(input());
    assert.equal(account.hasPassword, true);
    assert.equal(account.hasTotpSecret, true);
    for (const data of [
      readFileSync(path.join(dir, "vault.json"), "utf8"),
      JSON.stringify(account),
      JSON.stringify(store.export()),
    ]) {
      assert.ok(!data.includes(password));
      assert.ok(!data.includes(seed));
      assert.ok(!data.includes("otpauth://"));
    }
    const reopened = new Store(dir, c);
    assert.equal(
      (await reopened.bastionCode(account.id, 59999)).code,
      "287082",
    );
    const exported = store.export();
    await store.import({
      ...exported,
      bastionAccounts: exported.bastionAccounts.map((a) => ({
        ...a,
        password,
        seedInput: seed,
        encryptedSeed: seed,
        totpEnabled: true,
      })),
    });
    const imported = store.bastionAccounts().find((a) => a.id !== account.id)!;
    assert.equal(imported.hasPassword, false);
    assert.equal(imported.hasTotpSecret, false);
    assert.equal(imported.totpEnabled, false);
    await store.saveBastionAccount({
      ...input(),
      id: account.id,
      passwordAction: "keep",
      password: undefined,
      seedAction: "keep",
      seedInput: undefined,
      name: "改名",
    });
    assert.equal((await store.bastionCode(account.id, 59999)).code, "287082");
    await store.saveBastionAccount({
      ...input(),
      id: account.id,
      passwordAction: "clear",
      seedAction: "clear",
    });
    const cleared = store.bastionAccounts().find((a) => a.id === account.id)!;
    assert.equal(cleared.hasPassword, false);
    assert.equal(cleared.hasTotpSecret, false);
    assert.equal(cleared.totpEnabled, false);
    await assert.rejects(() => store.bastionCode(account.id), /未启用/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
test("encryption unavailable never falls back or leaks library errors, failed writes roll back", async () => {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-account-fail-"));
  try {
    const store = new Store(dir, {
      encrypt: async () => {
        throw new Error(password + seed);
      },
      decrypt: async () => {
        throw new Error(seed);
      },
    });
    await assert.rejects(
      () => store.saveBastionAccount(input()),
      (e) =>
        /系统加密/.test(String(e)) &&
        !String(e).includes(password) &&
        !String(e).includes(seed),
    );
    assert.equal(store.bastionAccounts().length, 0);
    const safe = new Store(dir, cipher());
    const [a] = await safe.saveBastionAccount(input());
    mkdirSync(path.join(dir, "vault.json.tmp"));
    await assert.rejects(
      () =>
        safe.saveBastionAccount({
          ...input(),
          id: a.id,
          passwordAction: "clear",
          seedAction: "clear",
        }),
      /写入失败/,
    );
    assert.equal(safe.bastionAccounts()[0].hasTotpSecret, true);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
test("update, disabled seed retention, deletion and pending code invalidation", async () => {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-account-update-"));
  try {
    const c = cipher();
    let release: (() => void) | undefined;
    const store = new Store(dir, {
      ...c,
      decrypt: async (s) => {
        await new Promise<void>((r) => (release = r));
        return c.decrypt(s);
      },
    });
    const [a] = await store.saveBastionAccount(input());
    const pending = store.bastionCode(a.id, 59999);
    await store.saveBastionAccount({
      ...input(),
      id: a.id,
      passwordAction: "keep",
      seedAction: "keep",
      totpEnabled: false,
    });
    release!();
    await assert.rejects(() => pending, /配置已改变/);
    assert.equal(store.bastionAccounts()[0].hasTotpSecret, true);
    await store.deleteBastionAccount(a.id);
    assert.equal(store.bastionAccounts().length, 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("portal password main-only callback detects concurrent revision/deletion and never echoes decrypt errors", async () => {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-account-password-"));
  try {
    const c = cipher();
    let release: (() => void) | undefined,
      delayDecrypt = false;
    const store = new Store(dir, {
      ...c,
      decrypt: async (value) => {
        if (delayDecrypt)
          await new Promise<void>((resolve) => (release = resolve));
        return c.decrypt(value);
      },
    });
    const [a] = await store.saveBastionAccount(input());
    assert.equal(
      await store.withBastionPassword(
        a.id,
        async (value) => value === password,
      ),
      true,
    );
    delayDecrypt = true;
    let called = false;
    const pending = store.withBastionPassword(a.id, async () => {
      called = true;
      return true;
    });
    await store.deleteBastionAccount(a.id);
    release!();
    await assert.rejects(() => pending, /配置已改变/);
    assert.equal(called, false);
    await assert.rejects(
      () => store.withBastionPassword("not-present", async () => true),
      /已不存在/,
    );
    const [b] = await store.saveBastionAccount(input());
    const broken = new Store(dir, {
      ...c,
      decrypt: async () => {
        throw new Error(password + seed);
      },
    });
    await assert.rejects(
      () => broken.withBastionPassword(b.id, async () => true),
      (e) =>
        /无法解密/.test(String(e)) &&
        !String(e).includes(password) &&
        !String(e).includes(seed),
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
