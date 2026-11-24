// Historical <= 0.1.6 UI scenario; the npm alias now runs three-column-smoke.mts.
import { codeText, replaceCode, until } from "./ui-helpers.mts";
import { _electron as electron, type Page } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, rm, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture, waitFor } from "../tests/fixture";
const version = JSON.parse(await readFile("package.json", "utf8")).version;
const data = await mkdtemp(path.join(tmpdir(), "forterm-layout-ui-"));
const service = await fixture({ shellDelay: 250 });
const env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const executable =
  process.env.FORTERM_PACKAGED_EXE || (electronPath as unknown as string);
const args = process.env.FORTERM_PACKAGED_EXE ? [] : ["."];
let app = await electron.launch({ executablePath: executable, args, env });
const errors: string[] = [];
await mkdir("test-results", { recursive: true });
async function drag(page: Page, name: string, dx: number, dy: number) {
  const handle = page.getByRole("separator", { name, exact: true });
  await handle.hover(); // Wait for the measured layout and the native window to settle.
  const box = await handle.boundingBox();
  assert.ok(box);
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  assert.equal(await handle.getAttribute("data-dragging"), "true");
  await page.mouse.move(
    box.x + box.width / 2 + dx,
    box.y + box.height / 2 + dy,
    { steps: 12 },
  );
  await page.mouse.up();
  await page.waitForFunction(
    () => document.body.dataset.resizing === undefined,
  );
  return Number(await handle.getAttribute("aria-valuenow"));
}
async function bounds(page: Page) {
  return page.evaluate(() => {
    const rectangles = [
      ...document.querySelectorAll<HTMLElement>(
        ".sidebar,.right-panel,.terminal-view:not(.hidden),.editor-region,.file-region,.monitor-compact",
      ),
    ]
      .filter((e) => e.getClientRects().length)
      .map((e) => {
        const r = e.getBoundingClientRect();
        return { name: e.className, x: r.x, y: r.y, w: r.width, h: r.height };
      });
    return {
      width: innerWidth,
      height: innerHeight,
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      rectangles,
    };
  });
}
try {
  const page = await app.firstWindow();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  await page.evaluate(() => {
    (window as any).pointerTrace = [];
    for (const type of [
      "pointerdown",
      "pointerup",
      "pointercancel",
      "gotpointercapture",
      "lostpointercapture",
      "blur",
    ])
      window.addEventListener(
        type,
        (event) => {
          const e = event as PointerEvent;
          (window as any).pointerTrace.push({
            type,
            id: e.pointerId,
            buttons: e.buttons,
            primary: e.isPrimary,
            target: (e.target as HTMLElement)?.getAttribute?.("aria-label"),
          });
        },
        true,
      );
  });
  page.setDefaultTimeout(15000);
  page.on("pageerror", (e) => errors.push(e.message));
  await page.getByRole("heading", { name: /每一个连接/ }).waitFor();
  await page
    .getByRole("separator", { name: "连接栏宽度", exact: true })
    .hover();
  await page.waitForFunction(
    () =>
      document
        .querySelector('[aria-label="连接栏宽度"]')
        ?.getAttribute("aria-valuenow") === "254",
  );
  const sidebar = await page.locator(".sidebar").boundingBox();
  assert.ok(sidebar);
  await drag(page, "连接栏宽度", 55, 80);
  assert.ok(
    Math.abs(
      (await page.locator(".sidebar").boundingBox())!.width -
        sidebar.width -
        55,
    ) < 2,
  );
  const panel = await page.locator(".right-panel").boundingBox();
  assert.ok(panel);
  await drag(page, "文件监控面板宽度", -60, 30);
  assert.ok(
    (await page.locator(".right-panel").boundingBox())!.width >
      panel.width + 50,
  );
  // Preferred dimensions survive a genuine application restart.
  await page.waitForFunction(
    () =>
      JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}").panel > 370,
  );
  // Even a close before the debounce must save the last visible dimensions.
  await page.evaluate(() => localStorage.removeItem("forterm.layout.v1"));
  await app.close();
  app = await electron.launch({ executablePath: executable, args, env });
  const p = await app.firstWindow();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  p.setDefaultTimeout(15000);
  p.on("pageerror", (e) => errors.push(e.message));
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  assert.ok(
    Math.abs((await p.locator(".sidebar").boundingBox())!.width - 309) < 2,
  );
  assert.ok(
    Math.abs((await p.locator(".right-panel").boundingBox())!.width - 380) < 2,
  );
  const id = await p.evaluate(
    (port) =>
      (window as any).forterm.connect({
        protocol: "sftp",
        host: "127.0.0.1",
        port,
        username: "ticket",
        password: "one-time@password",
        title: "本机服务 · 拖动布局",
        temporary: true,
      }),
    service.port,
  );
  await p.getByRole("button", { name: "信任并连接", exact: true }).click();
  await p
    .getByRole("button", { name: "此会话的资源监控", exact: true })
    .click();
  await p.locator(".monitor-full").waitFor();
  assert.equal(
    await p
      .getByRole("separator", { name: "文件与监控区域比例", exact: true })
      .count(),
    0,
  );
  await p.getByRole("button", { name: "在此会话打开终端" }).click();
  await until(p, async () =>
    (await (window as any).forterm.bootstrap()).sessions.some(
      (s: any) => s.terminalStatus === "ready",
    ),
  );
  await waitFor(() => service.resizes.length > 0);
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  await p
    .locator(".file-row")
    .filter({ hasText: "hello.txt" })
    .getByTitle("在线编辑", { exact: true })
    .click();
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await editor.waitFor();
  await replaceCode(p, "拖动期间的未保存草稿");
  const beforeInput = service.received;
  await p.getByRole("button", { name: "刷新状态", exact: true }).click();
  await p.locator(".metric-grid").waitFor();
  await editor.focus();
  await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption("bottom");
  await editor.focus();
  const editorBefore = (await p.locator(".editor-region").boundingBox())!
    .height;
  await drag(p, "编辑器高度", 40, -65);
  assert.ok(
    (await p.locator(".editor-region").boundingBox())!.height >
      editorBefore + 50,
  );
  assert.equal(
    await editor.evaluate((element) => document.activeElement === element),
    true,
    "drag preserves editor focus",
  );
  const expandedHeight = (await p.locator(".editor-region").boundingBox())!
    .height;
  await p.getByTitle("收起编辑器", { exact: true }).click();
  assert.equal(
    await p.getByRole("separator", { name: "编辑器高度", exact: true }).count(),
    0,
  );
  await p.getByTitle("展开编辑器", { exact: true }).click();
  assert.equal(await codeText(p), "拖动期间的未保存草稿");
  assert.ok(
    Math.abs(
      (await p.locator(".editor-region").boundingBox())!.height -
        expandedHeight,
    ) < 2,
  );
  const monitorBefore = (await p.locator(".monitor-compact").boundingBox())!
    .height;
  await drag(p, "文件与监控区域比例", 30, -45);
  assert.ok(
    (await p.locator(".monitor-compact").boundingBox())!.height >
      monitorBefore + 15,
  );
  const resizeBefore = service.resizes.length;
  await drag(p, "文件监控面板宽度", 75, 50);
  await waitFor(() => service.resizes.length > resizeBefore);
  assert.ok(service.resizes.every((r) => r.cols >= 2 && r.rows >= 1));
  assert.equal(
    service.received,
    beforeInput,
    "dragging must not send any shell input",
  );
  assert.equal(await codeText(p), "拖动期间的未保存草稿");
  // A completed upload and expanded queue must share the same bounded workspace.
  const upload = path.join(data, "layout-upload.txt");
  await writeFile(upload, "synthetic layout upload");
  await app.evaluate(({ dialog }, file) => {
    (globalThis as any).originalOpenDialog = dialog.showOpenDialog;
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: [file],
    })) as typeof dialog.showOpenDialog;
  }, upload);
  await p.getByTitle("上传文件", { exact: true }).click();
  await p.locator(".transfers summary").waitFor();
  await until(p, async () =>
    (await (window as any).forterm.bootstrap()).transfers.every(
      (t: any) => t.state === "done",
    ),
  );
  await app.evaluate(({ dialog }) => {
    dialog.showOpenDialog = (globalThis as any).originalOpenDialog;
  });
  await p.locator(".transfers summary").click();
  assert.equal(await codeText(p), "拖动期间的未保存草稿");
  const withQueue = await bounds(p);
  assert.ok(
    withQueue.rectangles.every(
      (r) => r.h > 0 && r.y + r.h <= withQueue.height + 1,
    ),
  );
  await p.screenshot({ path: `test-results/layout-transfers-${version}.png` });
  await p.getByRole("button", { name: "清理已结束记录", exact: true }).click();
  // Keyboard, reset, capture cancellation, and subsequent pointer gestures.
  const handle = p.getByRole("separator", { name: "连接栏宽度", exact: true });
  await handle.focus();
  const keyboardBefore = Number(await handle.getAttribute("aria-valuenow"));
  await p.keyboard.press("ArrowRight");
  assert.equal(
    Number(await handle.getAttribute("aria-valuenow")),
    keyboardBefore + 10,
  );
  await p.keyboard.press("Enter");
  assert.equal(Number(await handle.getAttribute("aria-valuenow")), 254);
  await p.evaluate(() => {
    const handle = document.querySelector(
      '[aria-label="连接栏宽度"]',
    ) as HTMLElement;
    handle.addEventListener(
      "pointerdown",
      (e) => ((window as any).testPointer = (e as PointerEvent).pointerId),
      { once: true },
    );
  });
  const hb = (await handle.boundingBox())!;
  await p.mouse.move(hb.x + 3, hb.y + 50);
  await p.mouse.down();
  await p.mouse.move(hb.x + 25, hb.y + 70);
  await p.evaluate(() =>
    document.querySelector('[aria-label="连接栏宽度"]')!.dispatchEvent(
      new PointerEvent("pointercancel", {
        pointerId: (window as any).testPointer,
        bubbles: true,
      }),
    ),
  );
  assert.equal(
    await p.evaluate(() => document.body.dataset.resizing),
    undefined,
  );
  await p.mouse.up();
  await drag(p, "连接栏宽度", 12, 0);
  const localId = await p.evaluate(() => (window as any).forterm.local());
  await p.getByTitle("终端分屏", { exact: true }).click();
  const split = p.getByRole("separator", {
    name: "双终端分屏比例",
    exact: true,
  });
  const originalRatio = Number(await split.getAttribute("aria-valuenow"));
  await drag(p, "双终端分屏比例", 30, 60);
  assert.ok(Number(await split.getAttribute("aria-valuenow")) > originalRatio);
  await p.getByTitle("切换分屏方向", { exact: true }).click();
  const horizontalBefore = Number(await split.getAttribute("aria-valuenow"));
  await drag(p, "双终端分屏比例", 65, 30);
  assert.ok(
    Number(await split.getAttribute("aria-valuenow")) > horizontalBefore,
  );
  await p.screenshot({ path: `test-results/layout-split-${version}.png` });
  await p.evaluate((id) => (window as any).forterm.close(id), localId);
  await p.getByTitle("切换侧面板", { exact: true }).click();
  assert.equal(
    await p
      .getByRole("separator", { name: "文件监控面板宽度", exact: true })
      .count(),
    0,
  );
  await p.getByTitle("切换侧面板", { exact: true }).click();
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  // User-enabled monitoring pauses while closed and resumes without overlap or reauthentication.
  const expected = service.execRequests + 1;
  await p.getByLabel("每 10 秒刷新（仅面板打开时）").check();
  await waitFor(() => service.execRequests >= expected);
  await p.getByTitle("切换侧面板", { exact: true }).click();
  const hiddenCount = service.execRequests;
  await new Promise((r) => setTimeout(r, 10300));
  assert.equal(service.execRequests, hiddenCount);
  await p.getByTitle("切换侧面板", { exact: true }).click();
  await waitFor(() => service.execRequests > hiddenCount);
  await p.getByLabel("每 10 秒刷新（仅面板打开时）").uncheck();
  await p.screenshot({ path: `test-results/layout-workspace-${version}.png` });
  await drag(p, "连接栏宽度", 1600, 0);
  const sidebarMax = p.getByRole("separator", {
    name: "连接栏宽度",
    exact: true,
  });
  assert.equal(
    await sidebarMax.getAttribute("aria-valuenow"),
    await sidebarMax.getAttribute("aria-valuemax"),
  );
  await sidebarMax.dblclick();
  assert.equal(Number(await sidebarMax.getAttribute("aria-valuenow")), 254);
  await drag(p, "文件监控面板宽度", 1600, 0);
  const rightMin = p.getByRole("separator", {
    name: "文件监控面板宽度",
    exact: true,
  });
  assert.equal(
    await rightMin.getAttribute("aria-valuenow"),
    await rightMin.getAttribute("aria-valuemin"),
  );
  await rightMin.focus();
  await p.keyboard.press("Enter");
  await drag(p, "编辑器高度", 0, -1400);
  const editorMax = p.getByRole("separator", {
    name: "编辑器高度",
    exact: true,
  });
  assert.equal(
    await editorMax.getAttribute("aria-valuenow"),
    await editorMax.getAttribute("aria-valuemax"),
  );
  assert.ok((await p.locator(".terminal-area").boundingBox())!.height >= 150);
  await editorMax.focus();
  await p.keyboard.press("Enter");
  await p.waitForFunction(
    () =>
      JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}").editor ===
      250,
  );
  const preferred = await p.evaluate(() =>
    localStorage.getItem("forterm.layout.v1"),
  );
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].setSize(1050, 650),
  );
  await p.waitForFunction(() => innerWidth <= 1050);
  await p.waitForFunction(
    (saved) => localStorage.getItem("forterm.layout.v1") === saved,
    preferred,
  );
  let measured = await bounds(p);
  assert.equal(measured.overflow, false);
  assert.ok(
    measured.rectangles.every(
      (r) =>
        r.w > 0 &&
        r.h > 0 &&
        r.x >= -1 &&
        r.y >= -1 &&
        r.x + r.w <= measured.width + 1 &&
        r.y + r.h <= measured.height + 1,
    ),
    JSON.stringify(measured),
  );
  await p.screenshot({ path: `test-results/layout-compact-${version}.png` });
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(1.25),
  );
  await new Promise((r) => setTimeout(r, 300));
  measured = await bounds(p);
  assert.equal(measured.overflow, false);
  assert.ok(
    measured.rectangles.every(
      (r) =>
        r.w > 0 &&
        r.h > 0 &&
        r.x + r.w <= measured.width + 1 &&
        r.y + r.h <= measured.height + 1,
    ),
    JSON.stringify(measured),
  );
  await drag(p, "编辑器高度", 0, -40);
  const capture = await app.evaluate(async ({ BrowserWindow }) =>
    (await BrowserWindow.getAllWindows()[0].capturePage())
      .toPNG()
      .toString("base64"),
  );
  await writeFile(
    `test-results/layout-zoom-${version}.png`,
    Buffer.from(capture, "base64"),
  );
  await app.evaluate(({ BrowserWindow }) => {
    BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(1);
    BrowserWindow.getAllWindows()[0].setSize(1440, 940);
  });
  assert.equal(await codeText(p), "拖动期间的未保存草稿");
  assert.equal(
    await readFile(path.join(service.directory, "hello.txt"), "utf8"),
    "hello 文件\n",
  );
  await p.getByTitle("重置布局", { exact: true }).click();
  await p.waitForFunction(
    () =>
      JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}").editor ===
      250,
  );
  assert.equal(
    Number(
      await p
        .getByRole("separator", { name: "连接栏宽度", exact: true })
        .getAttribute("aria-valuenow"),
    ),
    254,
  );
  assert.equal(service.authentications, 1);
  assert.equal(service.sftpRequests, 1);
  assert.deepEqual(errors, []);
  await p.evaluate((id) => (window as any).forterm.setDirty(id, false), id);
  console.log(
    "PASS: real pointer drag of five divider types; capture/cancel/reuse; keyboard/reset; persisted restart; live SSH PTY resize; draft and input isolation; hidden monitor pause/resume; minimum window and 125% zoom bounds; one authentication",
  );
} catch (error) {
  await app
    .windows()[0]
    ?.screenshot({ path: `test-results/layout-failure-${version}.png` })
    .catch(() => {});
  console.log(
    await app
      .windows()[0]
      ?.locator("body")
      .innerText()
      .catch(() => "unavailable"),
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
  await service.close();
  await rm(data, { recursive: true, force: true });
}
