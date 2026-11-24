import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, rm, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import assert from "node:assert/strict";
import { setTimeout as delay } from "node:timers/promises";
async function until(page, predicate, arg) {
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    if (await page.evaluate(predicate, arg)) return;
    await delay(75);
  }
  throw new Error(
    "UI state did not reach the expected result within 15 seconds",
  );
}
const data = await mkdtemp(path.join(tmpdir(), "forterm-ui-"));
await mkdir("test-results", { recursive: true });
const env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const packaged = process.env.FORTERM_PACKAGED_EXE;
const executable = packaged || electronPath;
const baseArgs = packaged ? [] : ["."];
const app = await electron.launch({
  executablePath: executable,
  args: baseArgs,
  env,
});
const errors = [];
try {
  const page = await app.firstWindow();
  page.setDefaultTimeout(15000);
  page.on("pageerror", (e) => errors.push(e.message));
  await page.waitForFunction(() => !!window.forterm);
  assert.equal(
    (await page.evaluate(() => window.forterm.bootstrap())).reports.length,
    0,
  );
  await page.getByRole("heading", { name: /每一个连接/ }).waitFor();
  await page.screenshot({ path: "test-results/welcome.png" });
  await page
    .getByRole("button", { name: "新建连接", exact: true })
    .first()
    .click();
  await page.getByLabel("连接名称", { exact: true }).fill("本地测试");
  await page.getByLabel("地址", { exact: true }).fill("127.0.0.1");
  await page.getByLabel("用户名", { exact: true }).fill("test");
  await page.getByRole("button", { name: "仅保存", exact: true }).click();
  await page.locator(".saved-connection").waitFor();
  await page.getByRole("button", { name: "打开本地终端", exact: true }).click();
  await until(page, async () => {
    const b = await window.forterm.bootstrap();
    return b.sessions.some(
      (s) => s.protocol === "local" && s.status === "ready",
    );
  });
  const id = await page.evaluate(
    async () =>
      (await window.forterm.bootstrap()).sessions.find(
        (s) => s.protocol === "local",
      ).id,
  );
  await page.evaluate(({ id, command }) => window.forterm.input(id, command), {
    id,
    command:
      process.platform === "win32"
        ? "Write-Output '__FORTERM_UI__'\r"
        : "printf '__FORTERM_UI__\\n'\r",
  });
  await until(
    page,
    async (id) =>
      (await window.forterm.attach(id)).includes("__FORTERM_UI__\r\n"),
    id,
  );
  await page.screenshot({ path: "test-results/local-terminal.png" });
  // A second executable invocation must deliver a report to the existing instance.
  const nativeEntries = packaged && process.platform === "win32";
  const child = spawn(
    nativeEntries
      ? path.join(path.dirname(packaged), "forterm-diagnose.exe")
      : executable,
    [
      ...baseArgs,
      ...(nativeEntries ? [] : ["--diagnose"]),
      "-newtab",
      "PRIVATE_LABEL",
      "-url",
      "ssh://PRIVATE_USER:PRIVATE_SECRET@127.0.0.1:1",
      "--opaque",
      "PRIVATE_OPAQUE",
    ],
    { env, stdio: "ignore" },
  );
  const childClosed = new Promise((resolve) => child.once("exit", resolve));
  await page.getByRole("heading", { name: "看清控制器如何启动会话" }).waitFor();
  await page.locator(".report-card").waitFor();
  const text = await page.locator(".report-card").innerText();
  for (const secret of [
    "PRIVATE_LABEL",
    "PRIVATE_USER",
    "PRIVATE_SECRET",
    "PRIVATE_OPAQUE",
  ])
    assert.ok(!text.includes(secret));
  await page.screenshot({ path: "test-results/diagnosis.png" });
  await childClosed;
  const state = await page.evaluate(() => window.forterm.bootstrap());
  assert.equal(state.sessions.length, 1, "diagnosis must not connect");
  assert.equal(state.reports.length, 1);
  assert.equal(state.profiles.length, 1);
  const ftp = spawn(
    nativeEntries ? path.join(path.dirname(packaged), "xftp.exe") : executable,
    [
      ...baseArgs,
      ...(nativeEntries ? [] : ["--entry=xftp"]),
      "-url",
      "ssh://ticket:unused@127.0.0.1:1",
    ],
    { env, stdio: "ignore" },
  );
  await until(page, async () =>
    (await window.forterm.bootstrap()).sessions.some(
      (s) => s.protocol === "sftp",
    ),
  );
  const newWindow = app.waitForEvent("window", { timeout: 15000 });
  const term = spawn(
    nativeEntries
      ? path.join(path.dirname(packaged), "xshell.exe")
      : executable,
    [...baseArgs, "-newwin", "ssh://ticket:unused@127.0.0.1:1"],
    { env, stdio: "ignore" },
  );
  const second = await newWindow;
  second.setDefaultTimeout(15000);
  await second.waitForFunction(() => !!window.forterm);
  const finalState = await second.evaluate(() => window.forterm.bootstrap());
  assert.equal(finalState.sessions.length, 3);
  assert.equal(app.windows().length, 2);
  // All windows observe session removal in the shared workspace.
  await second.evaluate((id) => window.forterm.close(id), id);
  await until(
    page,
    async (id) =>
      (await window.forterm.bootstrap()).sessions.every((s) => s.id !== id),
    id,
  );
  assert.deepEqual(errors, []);
  console.log(
    "PASS: UI render, saved connection, real local PTY, diagnosis/redaction, Xftp routing, new window and shared-session removal",
  );
} catch (error) {
  const pages = app.windows();
  console.log(
    "Smoke failure state:",
    await pages[0]
      ?.evaluate(async () => {
        const b = await window.forterm.bootstrap();
        return { sessions: b.sessions, reports: b.reports };
      })
      .catch(() => null),
  );
  console.log(
    "Native window count:",
    await app
      .evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().length)
      .catch(() => null),
  );
  throw error;
} finally {
  await app.close();
  await rm(data, { recursive: true, force: true });
}
