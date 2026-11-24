import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, readFileSync } from "node:fs";
import path from "node:path";
import { tmpdir } from "node:os";
import { randomBytes, createCipheriv, createDecipheriv } from "node:crypto";
import { setTimeout as delay } from "node:timers/promises";
import { Store } from "../src/main/store";
import { Sessions } from "../src/main/sessions";
import { Prompts } from "../src/main/prompts";
import { BastionService } from "../src/main/bastion-service";
import { DEFAULT_TOTP } from "../src/shared/bastion";
import type { BastionAccessSnapshot } from "../src/shared/bastion-access";
import { fixture, waitFor } from "./fixture";
import {
  bastionHttpFixture,
  LoopbackBastionProvider,
  portalPassword,
  portalToken,
  challengeSecret,
} from "./bastion-http-fixture";
const seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
async function harness(
  options: {
    timeout?: number;
    shell?: boolean;
    sftp?: boolean;
    unavailable?: boolean;
  } = {},
) {
  const dir = mkdtempSync(path.join(tmpdir(), "forterm-bastion-service-")),
    key = randomBytes(32);
  const store = new Store(dir, {
    encrypt: async (text) => {
      const iv = randomBytes(12),
        c = createCipheriv("aes-256-gcm", key, iv);
      return Buffer.concat([
        iv,
        c.update(text),
        c.final(),
        c.getAuthTag(),
      ]).toString("base64");
    },
    decrypt: async (text) => {
      const b = Buffer.from(text, "base64"),
        c = createDecipheriv("aes-256-gcm", key, b.subarray(0, 12));
      c.setAuthTag(b.subarray(-16));
      return Buffer.concat([
        c.update(b.subarray(12, -16)),
        c.final(),
      ]).toString();
    },
  });
  const input = {
    name: "合成门户",
    portalUrl: "https://bastion.example/",
    username: "portal-user",
    passwordAction: "replace" as const,
    password: portalPassword,
    seedAction: "replace" as const,
    seedInput: seed,
    totpEnabled: true,
    totp: { ...DEFAULT_TOTP },
  };
  await store.saveBastionAccount(input);
  const id = store.bastionAccounts()[0].id;
  const f = await fixture({
    shell: options.shell,
    sftp: options.sftp,
    shellDelay: 80,
  });
  const http = await bastionHttpFixture(f.port),
    provider = new LoopbackBastionProvider(http.url);
  let service: BastionService | undefined;
  const snapshots: BastionAccessSnapshot[] = [];
  const prompts = new Prompts((e) => {
    if (e.type === "prompt")
      queueMicrotask(() => prompts.answer(e.prompt.id, []));
  });
  const sessions = new Sessions(
    (e) => {
      if (e.type === "session") service?.observeSession(e.session);
      if (e.type === "session-removed") service?.sessionRemoved(e.id);
    },
    store,
    prompts,
  );
  service = new BastionService(
    store,
    sessions,
    () => (options.unavailable ? undefined : provider),
    (s) => snapshots.push(s),
    { requestTimeoutMs: options.timeout ?? 300, connectTimeoutMs: 2000 },
  );
  const engine = service;
  async function login() {
    http.behavior.challenge = false;
    const state = await engine.login(id);
    assert.equal(state.status, "authenticated");
  }
  async function selection() {
    const a = (await engine.assets(id)).items[0];
    return { a, account: a.accounts[0] };
  }
  return {
    dir,
    store,
    id,
    input,
    f,
    http,
    provider,
    engine,
    sessions,
    snapshots,
    login,
    selection,
    async close() {
      engine.close();
      sessions.closeAll();
      prompts.close();
      await http.close();
      await f.close();
      rmSync(dir, { recursive: true, force: true });
    },
  };
}
function op(h: Awaited<ReturnType<typeof harness>>, id: string) {
  return h.engine.snapshot().operations.find((o) => o.id === id)!;
}
function noSecrets(value: unknown) {
  const s = JSON.stringify(value);
  for (const secret of [
    portalPassword,
    portalToken,
    challengeSecret,
    seed,
    "GRANT_SECRET_",
    "CURSOR_SECRET",
    "ASSET_A",
    "ACCOUNT_A",
    "one-time@password",
  ])
    assert.ok(
      !s.includes(secret),
      "public projection contains private contract data",
    );
}

test("unregistered provider is unavailable and never decrypts or sends portal traffic", async () => {
  const h = await harness({ unavailable: true });
  try {
    assert.equal(h.engine.state(h.id).status, "unavailable");
    assert.throws(() => h.engine.login(h.id), /待适配/);
    assert.throws(() => h.engine.assets(h.id), /待适配/);
    assert.equal(Object.keys(h.http.counts).length, 0);
    noSecrets(h.engine.snapshot());
  } finally {
    await h.close();
  }
});
test("HTTP login challenge is deduplicated; manual and explicit stored TOTP submit only on demand", async () => {
  const h = await harness();
  try {
    const one = h.engine.login(h.id),
      two = h.engine.login(h.id);
    assert.equal(one, two);
    const challenge = await one;
    assert.equal(challenge.status, "mfa");
    assert.equal(h.http.counts.login, 1);
    await delay(30);
    assert.equal(h.http.counts.respond ?? 0, 0);
    const a = h.engine.respond(h.id, challenge.challenge!.ref, {
        code: "123456",
      }),
      b = h.engine.respond(h.id, challenge.challenge!.ref, { code: "654321" });
    assert.equal(a, b);
    assert.equal((await a).status, "authenticated");
    assert.equal(h.http.counts.respond, 1);
    await h.engine.logout(h.id);
    assert.equal(h.http.counts.logout, 1);
    const next = await h.engine.login(h.id);
    assert.equal(
      (
        await h.engine.respond(h.id, next.challenge!.ref, {
          useStoredTotp: true,
        })
      ).status,
      "authenticated",
    );
    assert.equal(h.http.counts.respond, 2);
    assert.match(
      h.http.seen.filter((r) => r.path === "respond").at(-1)!.body.code,
      /^\d{6}$/,
    );
    noSecrets(h.snapshots);
    noSecrets(h.store.export());
    assert.ok(
      !readFileSync(path.join(h.dir, "vault.json"), "utf8").includes(
        portalPassword,
      ),
    );
  } finally {
    await h.close();
  }
});
test("opaque paginated asset/account/protocol references drive one HTTP exchange and a real ready SSH; logout preserves it", async () => {
  const h = await harness();
  try {
    await h.login();
    const first = await h.engine.assets(h.id, "linux");
    noSecrets(first);
    const second = await h.engine.assets(h.id, "linux", first.nextCursor);
    assert.equal(second.items[0].name, "测试资产 B");
    await assert.rejects(
      async () => h.engine.assets(h.id, "different", first.nextCursor),
      /分页/,
    );
    const asset = first.items[0],
      account = asset.accounts[0];
    assert.throws(
      () =>
        h.engine.open(h.id, asset.ref, second.items[0].accounts[0].ref, "ssh"),
      /授权引用/,
    );
    const a = h.engine.open(h.id, asset.ref, account.ref, "ssh"),
      b = h.engine.open(h.id, asset.ref, account.ref, "ssh");
    assert.equal(a.id, b.id);
    await waitFor(() => op(h, a.id).status === "connecting");
    assert.notEqual(h.sessions.list()[0]?.terminalStatus, "ready");
    await waitFor(() => op(h, a.id).status === "ready");
    assert.equal(h.f.authentications, 1);
    assert.equal(h.http.counts.exchange, 1);
    assert.equal(h.http.counts.request, 1);
    noSecrets(h.engine.snapshot());
    assert.equal(
      h.http.seen.find((r) => r.path === "request")!.body.account,
      "ACCOUNT_A",
    );
    const sid = op(h, a.id).sessionId!;
    await h.engine.logout(h.id);
    assert.equal(h.engine.state(h.id).status, "signed-out");
    h.sessions.input(sid, "after-portal-logout");
    await waitFor(() => h.f.received.includes("after-portal-logout"));
    assert.equal(
      (await h.sessions.listFiles(sid, "/")).files[0].name,
      "hello.txt",
    );
    assert.equal(h.f.authentications, 1);
    assert.throws(
      () => h.engine.open(h.id, asset.ref, account.ref, "ssh"),
      /先完成/,
    );
  } finally {
    await h.close();
  }
});
test("approval refresh never re-requests; approved, denied and acknowledged cancellation are distinct", async () => {
  for (const outcome of ["approved", "denied", "cancelled"] as const) {
    const h = await harness();
    try {
      await h.login();
      h.http.behavior.grant = "pending";
      const { a, account } = await h.selection();
      const opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
      await waitFor(() => op(h, opened.id).status === "pending-approval");
      assert.equal(h.http.counts.exchange ?? 0, 0);
      assert.equal(h.f.authentications, 0);
      if (outcome === "cancelled") {
        assert.equal((await h.engine.cancel(opened.id)).status, "cancelled");
        assert.equal(h.http.counts.cancel, 1);
        assert.match(op(h, opened.id).message, /服务端已确认/);
      } else {
        h.http.behavior.approval = outcome;
        h.http.behavior.delayAt = "approval";
        h.http.behavior.delayMs = 30;
        h.engine.refresh(opened.id);
        h.engine.refresh(opened.id);
        await waitFor(
          () =>
            op(h, opened.id).status ===
            (outcome === "approved" ? "ready" : "denied"),
        );
        assert.equal(h.http.counts.approval, 1);
      }
      assert.equal(h.http.counts.request, 1);
      assert.equal(h.http.counts.exchange ?? 0, outcome === "approved" ? 1 : 0);
      noSecrets(h.engine.snapshot());
    } finally {
      await h.close();
    }
  }
});
test("mutation timeouts are unknown, keep single-flight identity and do not replay a consumed ticket", async () => {
  for (const phase of ["login", "request", "exchange"]) {
    const h = await harness({ timeout: 60 });
    try {
      if (phase !== "login") await h.login();
      h.http.behavior.delayAt = phase;
      h.http.behavior.delayMs = 200;
      if (phase === "login") {
        assert.equal((await h.engine.login(h.id)).status, "unknown");
        await h.engine.login(h.id);
        assert.equal(h.http.counts.login, 1);
      } else {
        const { a, account } = await h.selection(),
          opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
        await waitFor(() => op(h, opened.id).status === "unknown");
        const duplicate = h.engine.open(h.id, a.ref, account.ref, "ssh");
        assert.equal(duplicate.id, opened.id);
        h.engine.refresh(opened.id);
        await h.engine.cancel(opened.id);
        assert.equal(h.http.counts[phase], 1);
        if (phase === "exchange") assert.equal(h.http.used.size, 1);
        await delay(220);
        assert.equal(h.f.authentications, 0);
      }
      noSecrets(h.engine.snapshot());
    } finally {
      await h.close();
    }
  }
});
test("expired identities/grants, changed configuration and foreign references cannot create a connection", async () => {
  const h = await harness();
  try {
    h.http.behavior.challenge = false;
    h.http.behavior.identityTtl = 70;
    await h.engine.login(h.id);
    await delay(100);
    assert.equal(h.engine.state(h.id).status, "expired");
    assert.throws(() => h.engine.assets(h.id), /先完成/);
    h.http.behavior.identityTtl = 60000;
    await h.login();
    const { a, account } = await h.selection();
    h.http.behavior.grant = "pending";
    h.http.behavior.grantTtl = 30;
    const opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
    await waitFor(() => op(h, opened.id).status === "pending-approval");
    await delay(40);
    assert.equal(h.engine.refresh(opened.id).status, "expired");
    assert.equal(h.http.counts.exchange ?? 0, 0);
    await h.store.saveBastionAccount({
      ...h.input,
      id: h.id,
      passwordAction: "keep",
      seedAction: "keep",
      name: "changed",
    });
    h.engine.reconcile();
    assert.equal(h.engine.state(h.id).status, "signed-out");
    await h.login();
    assert.throws(
      () => h.engine.open(h.id, a.ref, account.ref, "ssh"),
      /授权引用/,
    );
    assert.equal(h.f.authentications, 0);
    noSecrets(h.snapshots);
  } finally {
    await h.close();
  }
});
test("shell and SFTP readiness failures preserve other real SSH capabilities and never claim ready early", async () => {
  for (const protocol of ["ssh", "sftp"] as const) {
    const h = await harness({
      shell: protocol !== "ssh",
      sftp: protocol !== "sftp",
    });
    try {
      await h.login();
      const { a, account } = await h.selection();
      const opened = h.engine.open(h.id, a.ref, account.ref, protocol);
      await waitFor(() => op(h, opened.id).status === "error");
      const sid = op(h, opened.id).sessionId!;
      assert.equal(
        h.sessions.list().find((s) => s.id === sid)?.status,
        "ready",
      );
      if (protocol === "ssh")
        assert.ok((await h.sessions.listFiles(sid, "/")).files.length);
      else {
        await h.sessions.openTerminal(sid);
        h.sessions.input(sid, "still-usable");
        await waitFor(() => h.f.received.includes("still-usable"));
      }
      assert.equal(h.f.authentications, 1);
      assert.equal(h.http.counts.exchange, 1);
    } finally {
      await h.close();
    }
  }
});
test("cancel/logout during one-time exchange discard late results and expose no new SSH session", async () => {
  for (const action of ["cancel", "logout"] as const) {
    const h = await harness();
    try {
      await h.login();
      h.http.behavior.delayAt = "exchange";
      h.http.behavior.delayMs = 150;
      const { a, account } = await h.selection();
      const opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
      await waitFor(() => !!h.http.counts.exchange);
      if (action === "cancel") await h.engine.cancel(opened.id);
      else await h.engine.logout(h.id);
      assert.equal(op(h, opened.id).status, "unknown");
      await delay(180);
      assert.equal(h.f.authentications, 0);
      assert.equal(h.sessions.list().length, 0);
      assert.equal(h.http.counts.exchange, 1);
      noSecrets(h.snapshots);
    } finally {
      await h.close();
    }
  }
});
test("OTP active-view guard is rechecked after decrypt and provider errors never echo response secrets", async () => {
  const h = await harness();
  try {
    const challenge = await h.engine.login(h.id);
    const result = await h.engine.respond(
      h.id,
      challenge.challenge!.ref,
      { useStoredTotp: true },
      () => {
        throw new Error(portalToken);
      },
    );
    assert.equal(result.status, "error");
    assert.equal(h.http.counts.respond ?? 0, 0);
    noSecrets(result);
    h.http.behavior.challenge = false;
    await h.login();
    h.http.behavior.expireAt = "assets";
    await assert.rejects(() => h.engine.assets(h.id), /身份过期/);
    assert.equal(h.engine.state(h.id).status, "expired");
    noSecrets(h.snapshots);
    noSecrets(h.store.export());
  } finally {
    await h.close();
  }
});

test("abrupt HTTP login loss is unknown; approval timeout remains manually refreshable without duplicate requests", async () => {
  const h = await harness({ timeout: 60 });
  try {
    h.http.behavior.dropAt = "login";
    assert.equal((await h.engine.login(h.id)).status, "unknown");
    assert.equal(h.http.counts.login, 1);
    await h.engine.login(h.id);
    assert.equal(h.http.counts.login, 1);
    await h.engine.logout(h.id);
    h.http.behavior.dropAt = "";
    await h.login();
    h.http.behavior.grant = "pending";
    const { a, account } = await h.selection(),
      opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
    await waitFor(() => op(h, opened.id).status === "pending-approval");
    h.http.behavior.delayAt = "approval";
    h.http.behavior.delayMs = 150;
    h.engine.refresh(opened.id);
    await waitFor(() => op(h, opened.id).message.includes("查询未完成"));
    assert.equal(op(h, opened.id).status, "pending-approval");
    h.http.behavior.delayAt = "";
    h.engine.refresh(opened.id);
    await waitFor(() => op(h, opened.id).status === "ready");
    assert.equal(h.http.counts.request, 1);
    assert.equal(h.http.counts.approval, 2);
    assert.equal(h.http.counts.exchange, 1);
  } finally {
    await h.close();
  }
});
test("logout while shell opens retains authenticated SSH; mismatched exchange protocol never connects", async () => {
  const h = await harness();
  try {
    await h.login();
    const { a, account } = await h.selection(),
      opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
    await waitFor(
      () =>
        op(h, opened.id).status === "connecting" &&
        h.sessions.list()[0]?.status === "ready",
    );
    const sid = op(h, opened.id).sessionId!;
    await h.engine.logout(h.id);
    assert.equal(op(h, opened.id).status, "cancelled");
    await waitFor(
      () =>
        h.sessions.list().find((s) => s.id === sid)?.terminalStatus === "ready",
    );
    assert.equal(h.f.authentications, 1);
    h.sessions.input(sid, "preserve-authenticated");
    await waitFor(() => h.f.received.includes("preserve-authenticated"));
    await h.login();
    h.http.behavior.endpointProtocol = "sftp";
    const next = await h.selection(),
      bad = h.engine.open(h.id, next.a.ref, next.account.ref, "ssh");
    await waitFor(() => op(h, bad.id).status === "unknown");
    assert.equal(h.f.authentications, 1);
    assert.equal(h.sessions.list().length, 1);
    noSecrets(h.snapshots);
  } finally {
    await h.close();
  }
});
test("late provider login ignoring abort is disposed and cannot resurrect an explicitly logged-out identity", async () => {
  const h = await harness();
  try {
    const original = h.provider.login.bind(h.provider);
    let entered = false;
    h.provider.login = async (account, credentials, _signal) => {
      entered = true;
      const result = await original(
        account,
        credentials,
        new AbortController().signal,
      );
      await delay(80);
      return result;
    };
    const pending = h.engine.login(h.id);
    await waitFor(() => entered);
    await h.engine.logout(h.id);
    await pending;
    await delay(100);
    assert.equal(h.engine.state(h.id).status, "signed-out");
    assert.ok(h.provider.disposed.length > 0);
    assert.equal(h.f.authentications, 0);
    noSecrets(h.engine.snapshot());
  } finally {
    await h.close();
  }
});

test("pending authorization expires locally without polling and explicit manual password remains transient", async () => {
  const h = await harness();
  try {
    h.http.behavior.challenge = false;
    assert.equal(
      (await h.engine.login(h.id, { password: portalPassword })).status,
      "authenticated",
    );
    const { a, account } = await h.selection();
    h.http.behavior.grant = "pending";
    h.http.behavior.grantTtl = 50;
    const opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
    await waitFor(() => op(h, opened.id).status === "expired");
    assert.equal(h.http.counts.approval ?? 0, 0);
    assert.equal(h.http.counts.exchange ?? 0, 0);
    assert.equal(h.f.authentications, 0);
    noSecrets(h.store.export());
    noSecrets(h.engine.snapshot());
  } finally {
    await h.close();
  }
});

test("explicitly closing a connecting SSH updates the operation immediately without waiting for timeout", async () => {
  const h = await harness();
  try {
    await h.login();
    const { a, account } = await h.selection(),
      opened = h.engine.open(h.id, a.ref, account.ref, "ssh");
    await waitFor(() => op(h, opened.id).status === "connecting");
    h.sessions.close(op(h, opened.id).sessionId!);
    assert.equal(op(h, opened.id).status, "cancelled");
    assert.equal(h.http.counts.exchange, 1);
    assert.equal(h.sessions.list().length, 0);
  } finally {
    await h.close();
  }
});
