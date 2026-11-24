import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, rm, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { until } from "./ui-helpers.mts";
const data = await mkdtemp(path.join(tmpdir(), "forterm-credentials-ui-")),
  env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
  password = "synthetic-portal-password";
let app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
await mkdir("test-results", { recursive: true });
try {
  let p = await app.firstWindow();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const available = await app.evaluate(async ({ safeStorage }) =>
    safeStorage.isAsyncEncryptionAvailable(),
  );
  assert.ok(
    available,
    "host system encryption is unavailable; cannot claim native encrypted UI path",
  );
  await p.getByRole("button", { name: "新增堡垒机", exact: true }).click();
  await p.getByLabel("堡垒机配置名称", { exact: true }).fill("合成堡垒机");
  await p
    .getByLabel("堡垒机入口地址", { exact: true })
    .fill("https://bastion.example/");
  await p
    .getByLabel("堡垒机登录用户名", { exact: true })
    .fill("synthetic-user");
  await p.getByLabel("保存堡垒机登录密码", { exact: true }).check();
  await p.getByLabel("堡垒机登录密码", { exact: true }).fill(password);
  await p.getByLabel("启用手机验证器（TOTP）", { exact: true }).check();
  await p.getByLabel("验证器密钥", { exact: true }).fill(seed);
  await p.getByRole("button", { name: "保存账户配置", exact: true }).click();
  await p.getByText("登录密码已加密保存", { exact: false }).waitFor();
  const account = await p.evaluate(
    async () => (await (window as any).forterm.bootstrap()).bastionAccounts[0],
  );
  assert.ok(
    account.hasPassword && account.hasTotpSecret && account.totpEnabled,
  );
  const vault = await readFile(path.join(data, "vault.json"), "utf8");
  assert.ok(!vault.includes(seed) && !vault.includes(password));
  assert.ok(!JSON.stringify(account).includes(seed));
  await app.evaluate(() => {
    (globalThis as any).originalNow = Date.now;
    Date.now = () => 59000;
  });
  await p.evaluate(() => {
    (window as any).originalNow = Date.now;
    Date.now = () => 59000;
  });
  await app.evaluate(({ safeStorage }) => {
    const original = safeStorage.decryptStringAsync.bind(safeStorage);
    (globalThis as any).decodeCalls = 0;
    safeStorage.decryptStringAsync = (async (
      ...args: Parameters<typeof original>
    ) => {
      (globalThis as any).decodeCalls++;
      return original(...args);
    }) as typeof safeStorage.decryptStringAsync;
  });
  await p.getByRole("button", { name: "验证码", exact: true }).click();
  await p.getByRole("button", { name: "查看当前验证码", exact: true }).click();
  await until(
    p,
    () =>
      document.querySelector('[aria-label="当前验证码"]')?.textContent ===
      "287082",
    undefined,
  );
  await p.getByRole("button", { name: "复制当前码", exact: true }).click();
  assert.ok(
    await app.evaluate(
      async ({ clipboard }) => (await clipboard.readText()) === "287082",
    ),
    "native clipboard differs from public fixed-time vector",
  );
  await app.evaluate(() => (Date.now = () => 60000));
  await p.evaluate(() => (Date.now = () => 60000));
  await until(
    p,
    () =>
      document.querySelector('[aria-label="当前验证码"]')?.textContent ===
      "359152",
    undefined,
  );
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].hide(),
  );
  await until(
    p,
    () => !document.querySelector('[aria-label="当前验证码"]'),
    undefined,
  );
  const hidden = await app.evaluate(() => (globalThis as any).decodeCalls);
  await app.evaluate(() => (Date.now = () => 90000));
  await p.evaluate(() => (Date.now = () => 90000));
  await new Promise((r) => setTimeout(r, 700));
  assert.equal(
    await app.evaluate(() => (globalThis as any).decodeCalls),
    hidden,
  );
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  await p
    .getByRole("button", { name: "查看当前验证码", exact: true })
    .waitFor();
  assert.equal(await p.getByLabel("当前验证码", { exact: true }).count(), 0);
  await p.getByRole("button", { name: "查看当前验证码", exact: true }).click();
  await until(
    p,
    () =>
      !!document
        .querySelector('[aria-label="当前验证码"]')
        ?.textContent?.match(/^\d{6}$/),
    undefined,
  );
  await p.getByRole("button", { name: "关闭堡垒机账户", exact: true }).click();
  const closed = await app.evaluate(() => (globalThis as any).decodeCalls);
  await app.evaluate(() => (Date.now = () => 120000));
  await p.evaluate(() => (Date.now = () => 120000));
  await new Promise((r) => setTimeout(r, 700));
  assert.equal(
    await app.evaluate(() => (globalThis as any).decodeCalls),
    closed,
  );
  await app.evaluate(() => (Date.now = (globalThis as any).originalNow));
  await p.evaluate(() => (Date.now = (window as any).originalNow));
  await p.evaluate(async () => {
    await (window as any).forterm.saveBastionAccount({
      name: "第二个堡垒机 — 长名称与窄侧栏测试",
      portalUrl: "https://second.example/login",
      username: "second-user",
      passwordAction: "keep",
      seedAction: "keep",
      totpEnabled: false,
      totp: { algorithm: "SHA1", digits: 6, period: 30 },
    });
  });
  await until(
    p,
    () => document.querySelectorAll(".saved-bastion").length === 2,
    undefined,
  );
  await p.getByLabel("搜索连接", { exact: true }).fill("second-user");
  assert.equal(await p.locator(".saved-bastion").count(), 1);
  await p.getByLabel("搜索连接", { exact: true }).fill("");
  await p.getByRole("button", { name: "折叠堡垒机分组", exact: true }).click();
  assert.equal(await p.locator(".saved-bastion").count(), 0);
  await p.getByRole("button", { name: "折叠堡垒机分组", exact: true }).click();
  await app.close();
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: ["."],
    env,
  });
  p = await app.firstWindow();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  p.setDefaultTimeout(15000);
  p.on("pageerror", (e) => errors.push(e.message));
  await p.locator(".saved-bastion").first().waitFor();
  assert.equal(await p.locator(".saved-bastion").count(), 2);
  await p
    .getByRole("button", { name: "配置堡垒机 合成堡垒机", exact: true })
    .click();
  assert.equal(
    await p.getByLabel("堡垒机登录密码", { exact: true }).inputValue(),
    "",
  );
  assert.equal(
    await p.getByLabel("验证器密钥", { exact: true }).inputValue(),
    "",
  );
  await p
    .getByLabel("堡垒机配置名称", { exact: true })
    .fill("合成堡垒机已编辑");
  await p.getByRole("button", { name: "保存账户配置", exact: true }).click();
  await p.getByRole("button", { name: "关闭堡垒机账户", exact: true }).click();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.setSize(1050, 650);
    w.webContents.setZoomFactor(1.5);
  });
  await p
    .getByRole("button", { name: "配置堡垒机 合成堡垒机已编辑", exact: true })
    .click();
  await p.getByRole("button", { name: "清除已保存密码", exact: true }).click();
  await p.getByRole("button", { name: "清除已保存 key", exact: true }).click();
  await p.getByRole("button", { name: "保存账户配置", exact: true }).click();
  await until(
    p,
    async () => {
      const a = (
        await (window as any).forterm.bootstrap()
      ).bastionAccounts.find((a: any) => a.name === "合成堡垒机已编辑");
      return a && !a.hasPassword && !a.hasTotpSecret && !a.totpEnabled;
    },
    undefined,
  );
  await p.evaluate(
    () =>
      new Promise<void>((r) =>
        requestAnimationFrame(() => requestAnimationFrame(() => r())),
      ),
  );
  const image = await app.evaluate(async ({ BrowserWindow }) =>
    (await BrowserWindow.getAllWindows()[0].capturePage())
      .toPNG()
      .toString("base64"),
  );
  await writeFile(
    "test-results/bastion-accounts-0.1.5.png",
    Buffer.from(image, "base64"),
  );
  await p.getByRole("button", { name: "关闭堡垒机账户", exact: true }).click();
  assert.equal(await p.locator(".saved-bastion").count(), 2);
  assert.deepEqual(errors, []);
  await app.evaluate(async ({ clipboard }) => {
    const s = await clipboard.readText();
    if (/^\d{6}$/.test(s)) await clipboard.clear();
  });
  console.log(
    "PASS: actual safeStorage account/password/key save; RFC fixed-clock display/copy and boundary; native window hide and close stop generation; flags only on edit; preserve/update/clear; two left-sidebar entries/search/collapse/restart; 150% configuration access; screenshot with no code/seed/password",
  );
} catch (e) {
  await app
    .windows()[0]
    ?.screenshot({
      path: "test-results/accounts-failure-0.1.5.png",
      mask: [
        app
          .windows()[0]
          .locator('[aria-label="当前验证码"],input[type="password"]'),
      ],
    })
    .catch(() => {});
  console.log("account UI failed without dumping secret-bearing DOM");
  throw e;
} finally {
  await app
    .evaluate(({ dialog }) => {
      dialog.showMessageBox = (async () => ({
        response: 1,
        checkboxChecked: false,
      })) as typeof dialog.showMessageBox;
    })
    .catch(() => {});
  await app.close();
  await rm(data, { recursive: true, force: true });
}
