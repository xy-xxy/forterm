import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, rm, writeFile, mkdir, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { bastionHttpFixture } from "../tests/bastion-http-fixture";
import { DEFAULT_TOTP } from "../src/shared/bastion";
const version = JSON.parse(await readFile("package.json", "utf8")).version;
const dir = await mkdtemp(path.join(tmpdir(), "forterm-bastion-unavailable-")),
  http = await bastionHttpFixture(1);
const env = { ...process.env, FORTERM_TEST_DATA: dir };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
try {
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const id = await p.evaluate(
    async ({ url, totp }) => {
      const accounts = await (window as any).forterm.saveBastionAccount({
        name: "等待真实接口的堡垒机",
        portalUrl: url,
        username: "synthetic-user",
        passwordAction: "clear",
        seedAction: "clear",
        totpEnabled: false,
        totp,
      });
      return accounts[0].id;
    },
    { url: http.url, totp: DEFAULT_TOTP },
  );
  await p.getByRole("button", { name: "堡垒机账户", exact: true }).click();
  await p
    .getByRole("button", {
      name: "查看接入状态 等待真实接口的堡垒机",
      exact: true,
    })
    .click();
  const panel = p.getByRole("region", {
    name: "堡垒机接入 等待真实接口的堡垒机",
    exact: true,
  });
  await panel.getByRole("status").filter({ hasText: "待适配" }).waitFor();
  assert.equal(
    await panel
      .getByRole("button", { name: "登录堡垒机", exact: true })
      .isDisabled(),
    true,
  );
  assert.equal(
    await panel.getByLabel("搜索授权资产", { exact: true }).count(),
    0,
  );
  const result = await p.evaluate(async (id) => {
    const api = (window as any).forterm,
      failures = [];
    for (const fn of [
      () => api.bastionLogin(id, {}),
      () => api.bastionAssets(id, ""),
      () => api.bastionOpen(id, "arbitrary", "arbitrary", "ssh"),
      () =>
        api.bastionLogin(id, {
          url: "http://127.0.0.1/arbitrary",
          password: "synthetic",
        }),
    ]) {
      try {
        await fn();
        failures.push("unexpected success");
      } catch (e) {
        failures.push(String(e));
      }
    }
    return {
      failures,
      state: await api.bastionAccess(),
      apiKeys: Object.keys(api),
    };
  }, id);
  assert.ok(result.failures.every((s) => /待适配|输入格式无效/.test(s)));
  assert.equal(result.state.accounts[0].status, "unavailable");
  assert.deepEqual(result.state.operations, []);
  assert.ok(!result.apiKeys.some((s) => /fetch|http|requesturl/i.test(s)));
  assert.deepEqual(http.counts, {});
  // Native full-window capture at high zoom; disabled login is visible and honest.
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.setSize(1050, 650);
    w.webContents.setZoomFactor(1.5);
  });
  await panel
    .getByRole("button", { name: "登录堡垒机", exact: true })
    .scrollIntoViewIfNeeded();
  const measured = await panel
    .getByRole("button", { name: "登录堡垒机", exact: true })
    .evaluate((el) => {
      const r = el.getBoundingClientRect();
      return {
        x: r.x,
        y: r.y,
        right: r.right,
        bottom: r.bottom,
        width: innerWidth,
        height: innerHeight,
      };
    });
  assert.ok(
    measured.x >= 0 &&
      measured.y >= 0 &&
      measured.right <= measured.width &&
      measured.bottom <= measured.height,
  );
  await p.evaluate(
    () =>
      new Promise<void>((r) =>
        requestAnimationFrame(() => requestAnimationFrame(() => r())),
      ),
  );
  const png = await app.evaluate(async ({ BrowserWindow }) =>
    (await BrowserWindow.getAllWindows()[0].capturePage())
      .toPNG()
      .toString("base64"),
  );
  await mkdir("test-results", { recursive: true });
  await writeFile(
    `test-results/bastion-unavailable-${version}.png`,
    Buffer.from(png, "base64"),
  );
  const main = await readFile("dist/main/index.cjs", "utf8");
  for (const marker of [
    "PORTAL_LONG_PASSWORD_SYNTHETIC",
    "GRANT_SECRET_",
    "CURSOR_SECRET",
    "test loopback only",
  ])
    assert.ok(!main.includes(marker));
  assert.deepEqual(errors, []);
  assert.deepEqual(http.counts, {});
  console.log(
    "PASS: actual Electron unavailable provider state and disabled login, no fake assets, guarded IPC/no arbitrary HTTP proxy, zero portal traffic, test adapter absent from production main bundle, visible login at native 150% capture",
  );
} finally {
  await app.close();
  await http.close();
  await rm(dir, { recursive: true, force: true });
}
