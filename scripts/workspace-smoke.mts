// Historical <= 0.1.6 UI scenario; the npm alias now runs three-column-smoke.mts.
import { codeText, replaceCode, until } from "./ui-helpers.mts";
import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, rm, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture, waitFor } from "../tests/fixture";
const data = await mkdtemp(path.join(tmpdir(), "forterm-workspace-ui-"));
const f = await fixture();
await writeFile(path.join(f.directory, ".hidden"), "hidden");
await writeFile(path.join(f.directory, "z-large.log"), "x".repeat(100));
await mkdir(path.join(f.directory, "sub"));
await mkdir("test-results", { recursive: true });
const env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const executable =
  process.env.FORTERM_PACKAGED_EXE || (electronPath as unknown as string);
const appArgs = process.env.FORTERM_PACKAGED_EXE ? [] : ["."];
let app = await electron.launch({
  executablePath: executable,
  args: appArgs,
  env,
});
const errors: string[] = [];
try {
  const page = await app.firstWindow();
  page.setDefaultTimeout(15000);
  page.on("pageerror", (e) => errors.push(e.message));
  await page.getByRole("heading", { name: /每一个连接/ }).waitFor();
  await page.keyboard.press("Control+Shift+N");
  await page.getByRole("heading", { name: "新建连接", exact: true }).waitFor();
  await page.getByRole("button", { name: "关闭", exact: true }).click();
  await page.getByRole("button", { name: "会话心跳" }).click();
  assert.equal(await page.getByLabel("心跳间隔（秒）").inputValue(), "15");
  await page.screenshot({ path: "test-results/heartbeat-settings.png" });
  await page.getByLabel("心跳间隔（秒）").fill("30");
  await page.getByRole("button", { name: "保存设置", exact: true }).click();
  await page.getByTitle("增大字体").click();
  await page.getByTitle("切换主题").click();
  assert.ok(await page.locator(".app.light").count());
  await page.screenshot({ path: "test-results/welcome-light.png" });
  // Relaunch to check settings and UI preferences survive a real application restart.
  await app.close();
  app = await electron.launch({
    executablePath: executable,
    args: appArgs,
    env,
  });
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  p.on("pageerror", (e) => errors.push(e.message));
  await p.locator(".app.light").waitFor();
  assert.equal(
    await p.evaluate(() =>
      (window as any).forterm
        .bootstrap()
        .then((b: any) => b.keepAlive.intervalSeconds),
    ),
    30,
  );
  assert.equal(
    await p.evaluate(
      () => JSON.parse(localStorage.getItem("forterm.workspace.v1")!).fontSize,
    ),
    15,
  );
  await p.getByTitle("切换主题").click();
  const id = await p.evaluate(
    (port) =>
      (window as any).forterm.connect({
        protocol: "sftp",
        host: "127.0.0.1",
        port,
        username: "ticket",
        password: "one-time@password",
        title: "本机合成服务 · 统一工作区",
        temporary: true,
      }),
    f.port,
  );
  await p.getByRole("button", { name: "信任并连接", exact: true }).click();
  await p.locator(".file-row").filter({ hasText: "hello.txt" }).waitFor();
  await p.getByLabel("筛选文件", { exact: true }).fill("hello");
  assert.equal(await p.locator(".file-row").count(), 1);
  await p.getByLabel("筛选文件", { exact: true }).fill("");
  await p.getByLabel("显示隐藏文件", { exact: false }).uncheck();
  assert.equal(
    await p.locator(".file-row").filter({ hasText: ".hidden" }).count(),
    0,
  );
  await p.getByLabel("文件排序").selectOption("size");
  await p.locator(".file-row").filter({ hasText: "sub" }).dblclick();
  await p.getByText("目录为空", { exact: true }).waitFor();
  await p.getByTitle("上级目录", { exact: true }).click();
  await p.locator(".file-row").filter({ hasText: "hello.txt" }).waitFor();
  await p.getByRole("button", { name: "在此会话打开终端" }).click();
  await p.locator(".terminal-view:not(.hidden)").waitFor();
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  await p.getByRole("button", { name: "刷新状态", exact: true }).click();
  await p.locator(".metric-grid").waitFor();
  await p.getByLabel("筛选文件", { exact: true }).fill("hello");
  await p
    .locator(".file-row")
    .filter({ hasText: "hello.txt" })
    .getByTitle("在线编辑", { exact: true })
    .click();
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await editor.waitFor();
  assert.equal(await codeText(p), "hello 文件\n");
  await replaceCode(p, "保存自统一工作区\r\n");
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await waitFor(() => f.execRequests === 1);
  await p.getByText("文件已保存", { exact: true }).waitFor();
  assert.match(
    await readFile(path.join(f.directory, "hello.txt"), "utf8"),
    /保存自统一工作区/,
  );
  await p.evaluate(
    (id) => (window as any).forterm.input(id, "ui-parallel\n"),
    id,
  );
  await waitFor(() => f.received.includes("ui-parallel"));
  await p.screenshot({ path: "test-results/unified-workspace.png" });
  const localId = await p.evaluate(() => (window as any).forterm.local());
  await p.getByTitle("终端分屏").click();
  assert.equal(await p.locator(".terminal-view:not(.hidden)").count(), 2);
  await p.getByTitle("切换分屏方向").click();
  assert.equal(await p.locator(".split-horizontal").count(), 1);
  await p
    .locator(".tab")
    .filter({ hasText: "本机合成服务" })
    .locator("button")
    .first()
    .click();
  await p.waitForFunction(
    (id) =>
      document.activeElement
        ?.closest(".terminal-view")
        ?.getAttribute("data-session-id") === id,
    id,
  );
  await p
    .locator(".tab")
    .filter({ hasText: "本地终端" })
    .locator("button")
    .first()
    .click();
  await p.waitForFunction(
    (id) =>
      document.activeElement
        ?.closest(".terminal-view")
        ?.getAttribute("data-session-id") === id,
    localId,
  );
  const inputBeforeSearch = f.received;
  await p
    .locator(`[data-session-id="${id}"]`)
    .getByLabel("搜索终端", { exact: true })
    .click();
  assert.equal(
    await p.evaluate(() => document.activeElement?.getAttribute("aria-label")),
    "搜索终端",
  );
  await p.keyboard.type("ONLY_SEARCH_QUERY");
  assert.equal(
    f.received,
    inputBeforeSearch,
    "search text must never reach shell stdin",
  );
  await p.screenshot({ path: "test-results/split-workspace.png" });
  await p.evaluate((id) => (window as any).forterm.close(id), localId);
  // No horizontal document overflow at the minimum supported window size.
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].setSize(1050, 650),
  );
  assert.ok(
    await p.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
  );
  await p.screenshot({ path: "test-results/unified-workspace-compact.png" });
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].setSize(1440, 940),
  );
  await replaceCode(p, "未保存草稿：不能丢失");
  await writeFile(path.join(f.directory, "hello.txt"), "远程的其他修改");
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await p.getByRole("status").filter({ hasText: "保存冲突" }).waitFor();
  assert.equal(await codeText(p), "未保存草稿：不能丢失");
  assert.equal(
    await readFile(path.join(f.directory, "hello.txt"), "utf8"),
    "远程的其他修改",
  );
  p.once("dialog", (dialog) => void dialog.dismiss());
  await p.getByRole("button", { name: "关闭编辑器", exact: true }).click();
  assert.equal(await codeText(p), "未保存草稿：不能丢失");
  // Native confirmation test seam affects only this isolated test process.
  await app.evaluate(({ dialog }) => {
    (globalThis as any).testWarnings = 0;
    dialog.showMessageBox = (async () => {
      (globalThis as any).testWarnings++;
      return { response: 0, checkboxChecked: false };
    }) as typeof dialog.showMessageBox;
  });
  const closed = await p.evaluate(
    (id) => (window as any).forterm.close(id),
    id,
  );
  assert.equal(closed, false);
  assert.equal(await codeText(p), "未保存草稿：不能丢失");
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].close(),
  );
  assert.equal(await app.evaluate(() => (globalThis as any).testWarnings), 2);
  assert.equal(await codeText(p), "未保存草稿：不能丢失");
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.reload(),
  );
  await new Promise((resolve) => setTimeout(resolve, 300));
  assert.equal(await codeText(p), "未保存草稿：不能丢失");
  // Auto refresh is active only while the visible monitor is mounted.
  const beforeAuto = f.execRequests;
  await p.getByLabel("每 10 秒刷新（仅面板打开时）").check();
  await waitFor(() => f.execRequests > beforeAuto);
  await p.getByRole("button", { name: "文件", exact: true }).click();
  const beforeHidden = f.execRequests;
  await new Promise((r) => setTimeout(r, 10300));
  assert.equal(f.execRequests, beforeHidden);
  f.dropConnections();
  await p.getByRole("status").filter({ hasText: "连接已断开" }).waitFor();
  assert.equal(await codeText(p), "未保存草稿：不能丢失");
  assert.equal(
    await p.getByRole("button", { name: "保存文件", exact: true }).isDisabled(),
    true,
  );
  await p.screenshot({ path: "test-results/editor-disconnected-draft.png" });
  assert.equal(
    f.authentications,
    1,
    "one authentication for all four capabilities",
  );
  assert.equal(f.sftpRequests, 1);
  assert.equal(f.shellRequests, 1);
  assert.deepEqual(errors, []);
  await p.evaluate((id) => (window as any).forterm.setDirty(id, false), id);
  console.log(
    "PASS: persisted heartbeat/theme/font, shared SSH shell/files/editor/monitor, explicit save, conflict protection, cancelled editor/session/window closure, hidden monitor stops, disconnected draft retention, split focus/search input isolation and compact layout",
  );
} catch (error) {
  const page = app.windows()[0];
  await page
    ?.screenshot({ path: "test-results/workspace-failure.png" })
    .catch(() => {});
  console.log(
    await page
      ?.locator("body")
      .innerText()
      .catch(() => "UI unavailable"),
  );
  throw error;
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
  await f.close();
  await rm(data, { recursive: true, force: true });
}
