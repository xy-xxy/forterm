import { replaceCode, until } from "./ui-helpers.mts";
import { _electron as electron, type Locator } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, rm, writeFile, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture } from "../tests/fixture";
const version = JSON.parse(await readFile("package.json", "utf8")).version;
const data = await mkdtemp(path.join(tmpdir(), "forterm-reachable-"));
const service = await fixture();
const imported = path.join(data, "import.json"),
  exported = path.join(data, "export.json");
await writeFile(
  imported,
  JSON.stringify({
    version: 1,
    profiles: [
      {
        protocol: "ssh",
        host: "127.0.0.1",
        port: 1,
        username: "synthetic",
        title: "导入的测试连接",
        group: "本机测试",
      },
    ],
  }),
);
const env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath:
    process.env.FORTERM_PACKAGED_EXE || (electronPath as unknown as string),
  args: process.env.FORTERM_PACKAGED_EXE ? [] : ["."],
  env,
});
await mkdir("test-results", { recursive: true });
const errors: string[] = [];
const measurements: unknown[] = [];
let scenario = "startup";
async function reachable(control: Locator) {
  await control.scrollIntoViewIfNeeded();
  const result = await control.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const failures: string[] = [];
    if (
      box.width <= 0 ||
      box.height <= 0 ||
      box.left < -1 ||
      box.top < -1 ||
      box.right > innerWidth + 1 ||
      box.bottom > innerHeight + 1
    )
      failures.push("outside viewport");
    for (
      let ancestor = element.parentElement;
      ancestor;
      ancestor = ancestor.parentElement
    ) {
      const style = getComputedStyle(ancestor),
        rect = ancestor.getBoundingClientRect();
      if (
        (/hidden|clip/.test(style.overflowY) && ancestor.scrollTop > 1) ||
        (/hidden|clip/.test(style.overflowX) && ancestor.scrollLeft > 1)
      )
        failures.push(
          "programmatically scrolled a non-user-scrollable ancestor: " +
            ancestor.className,
        );
      if (
        /auto|scroll|hidden|clip/.test(style.overflowY) &&
        (box.top < rect.top - 1 || box.bottom > rect.bottom + 1)
      )
        failures.push("clipped vertically by " + ancestor.className);
      if (
        /auto|scroll|hidden|clip/.test(style.overflowX) &&
        (box.left < rect.left - 1 || box.right > rect.right + 1)
      )
        failures.push("clipped horizontally by " + ancestor.className);
    }
    const hit = document.elementFromPoint(
      box.left + box.width / 2,
      box.top + box.height / 2,
    );
    if (!hit || !element.contains(hit))
      failures.push("center not hit-testable");
    return {
      failures,
      rect: { x: box.x, y: box.y, w: box.width, h: box.height },
      label: element.getAttribute("aria-label") || element.textContent,
      viewport: { width: innerWidth, height: innerHeight },
      scrolls: [
        ...document.querySelectorAll<HTMLElement>(
          ".sidebar,.sidebar-tools,.connection-tree,.app,.welcome,.sftp-workspace,.monitor-compact,.editor-region,.remote-editor,.topbar-actions",
        ),
      ]
        .filter((el) => el.contains(element))
        .map((el) => ({
          region: el.className,
          top: el.scrollTop,
          left: el.scrollLeft,
          clientHeight: el.clientHeight,
          scrollHeight: el.scrollHeight,
          overflow: getComputedStyle(el).overflow,
        })),
    };
  });
  measurements.push({ scenario, ...result });
  assert.deepEqual(result.failures, [], JSON.stringify(result));
}
async function capture(name: string) {
  await app
    .windows()[0]
    .evaluate(
      () =>
        new Promise<void>((resolve) =>
          requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
        ),
    );
  const png = await app.evaluate(async ({ BrowserWindow }) =>
    (await BrowserWindow.getAllWindows()[0].capturePage())
      .toPNG()
      .toString("base64"),
  );
  await writeFile(
    `test-results/${name}-${version}.png`,
    Buffer.from(png, "base64"),
  );
}
try {
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  p.on("pageerror", (e) => errors.push(e.message));
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
    w.setSize(1050, 650);
  });
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  await app.evaluate(
    ({ dialog }, files) => {
      (globalThis as any).importCount = 0;
      (globalThis as any).exportCount = 0;
      (globalThis as any).allowImport = false;
      dialog.showOpenDialog = (async () => {
        (globalThis as any).importCount++;
        return (globalThis as any).allowImport
          ? { canceled: false, filePaths: [files.imported] }
          : { canceled: true, filePaths: [] };
      }) as typeof dialog.showOpenDialog;
      dialog.showSaveDialog = (async () => {
        (globalThis as any).exportCount++;
        return { canceled: false, filePath: files.exported };
      }) as typeof dialog.showSaveDialog;
    },
    { imported, exported },
  );
  const sidebar = p.locator(".sidebar"),
    tools = p.getByRole("region", { name: "工作区工具", exact: true });
  async function sidebarControls() {
    await reachable(sidebar.locator(".new-connection"));
    await reachable(p.getByLabel("搜索连接", { exact: true }));
    await reachable(p.getByTitle("切换主题", { exact: true }));
    const lightBefore = await p.locator(".app").getAttribute("class");
    await p.getByTitle("切换主题", { exact: true }).click();
    assert.notEqual(await p.locator(".app").getAttribute("class"), lightBefore);
    await p.getByTitle("切换主题", { exact: true }).focus();
    await p.keyboard.press("Enter");
    assert.equal(await p.locator(".app").getAttribute("class"), lightBefore);
    // The scroll container itself is keyboard accessible, including its bottom.
    await tools.evaluate((el) => (el.scrollTop = 0));
    await tools.focus();
    await tools.press("PageDown");
    await p.waitForFunction(
      () => document.querySelector(".sidebar-tools")!.scrollTop > 0,
    );
    const importButton = sidebar.getByRole("button", {
      name: "导入",
      exact: true,
    });
    const exportButton = sidebar.getByRole("button", {
      name: "导出",
      exact: true,
    });
    await reachable(importButton);
    await tools.focus();
    for (let step = 0; step < 10; step++) {
      await p.keyboard.press("Tab");
      if (await importButton.evaluate((el) => document.activeElement === el))
        break;
    }
    assert.equal(
      await importButton.evaluate((el) => document.activeElement === el),
      true,
      "native Tab reaches import inside the scrolling tools",
    );
    await reachable(importButton);
    const imports = await app.evaluate(() => (globalThis as any).importCount);
    await p.keyboard.press("Enter");
    await p.waitForFunction(
      async () => !!(await (window as any).forterm.bootstrap()),
    );
    assert.equal(
      await app.evaluate(() => (globalThis as any).importCount),
      imports + 1,
    );
    await reachable(exportButton);
    const exports = await app.evaluate(() => (globalThis as any).exportCount);
    await exportButton.click();
    assert.equal(
      await app.evaluate(() => (globalThis as any).exportCount),
      exports + 1,
    );
    await p.waitForFunction(
      async () => !!(await (window as any).forterm.bootstrap()),
    );
    assert.equal(JSON.parse(await readFile(exported, "utf8")).version, 1);
    const shortcut = sidebar.getByRole("button", {
      name: "快捷键与操作",
      exact: true,
    });
    await reachable(shortcut);
    await shortcut.click();
    await reachable(p.getByRole("button", { name: "关闭快捷键", exact: true }));
    await p.getByRole("button", { name: "关闭快捷键", exact: true }).click();
    const heart = sidebar.getByRole("button", { name: /会话心跳/ });
    await reachable(heart);
    await heart.click();
    const save = p.getByRole("button", { name: "保存设置", exact: true });
    await reachable(save);
    await save.click();
    await reachable(sidebar.locator(".new-connection"));
    await sidebar.locator(".new-connection").focus();
    await p.keyboard.press("Enter");
    await p.getByRole("heading", { name: "新建连接", exact: true }).waitFor();
    await reachable(p.getByRole("button", { name: "仅保存", exact: true }));
    await reachable(p.getByRole("button", { name: "关闭", exact: true }));
    await p.getByRole("button", { name: "关闭", exact: true }).click();
    await p
      .getByRole("heading", { name: "新建连接", exact: true })
      .waitFor({ state: "hidden" });
    await reachable(importButton);
    await reachable(exportButton);
    await reachable(p.getByTitle("切换主题", { exact: true }));
  }
  for (const zoom of [1.25, 1.5]) {
    scenario = `empty-${zoom}`;
    await app.evaluate(
      ({ BrowserWindow }, z) =>
        BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(z),
      zoom,
    );
    await p.waitForFunction((z) => innerHeight <= Math.ceil(620 / z), zoom);
    await sidebarControls();
    const welcome = p.locator(".welcome");
    await reachable(
      welcome.getByRole("button", { name: "打开本地终端", exact: true }),
    );
    const welcomeNew = welcome.getByRole("button", {
      name: "新建连接",
      exact: true,
    });
    await reachable(welcomeNew);
    await welcomeNew.click();
    await p.getByRole("button", { name: "关闭", exact: true }).click();
    await p
      .getByRole("heading", { name: "新建连接", exact: true })
      .waitFor({ state: "hidden" });
    await reachable(sidebar.getByRole("button", { name: "导出", exact: true }));
    await capture(`reachable-empty-${zoom === 1.5 ? 150 : 125}`);
  }
  await p.evaluate(async () => {
    for (let i = 1; i <= 40; i++)
      await (window as any).forterm.saveProfile({
        protocol: "ssh",
        host: "127.0.0.1",
        port: 1,
        username: "synthetic",
        group: "长列表测试",
        title: `测试连接 ${String(i).padStart(2, "0")}`,
      });
  });
  await p.reload();
  await p.locator(".saved-connection").first().waitFor();
  await app.evaluate(() => ((globalThis as any).allowImport = true));
  for (const zoom of [1.25, 1.5]) {
    scenario = `long-${zoom}`;
    await app.evaluate(
      ({ BrowserWindow }, z) =>
        BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(z),
      zoom,
    );
    const tree = p.getByRole("region", { name: "保存的连接", exact: true });
    await p.waitForFunction(() => {
      const el = document.querySelector(".connection-tree")!;
      return el.scrollHeight > el.clientHeight;
    });
    await tree.focus();
    const initial = await tree.evaluate((el) => el.scrollTop);
    await tree.press("PageDown");
    await p.waitForFunction(
      (before) =>
        document.querySelector(".connection-tree")!.scrollTop > before,
      initial,
    );
    const last = p.locator(".saved-connection").last();
    await last.locator("button").first().focus();
    const edit = last.getByRole("button", { name: "编辑连接", exact: true });
    await reachable(edit);
    await edit.click();
    await reachable(p.getByRole("button", { name: "仅保存", exact: true }));
    await p.getByRole("button", { name: "仅保存", exact: true }).click();
    const before = await p.evaluate(
      async () => (await (window as any).forterm.bootstrap()).profiles.length,
    );
    await sidebarControls();
    await p.waitForFunction(
      async (before) =>
        (await (window as any).forterm.bootstrap()).profiles.length ===
        before + 1,
      before,
    );
    await capture(`reachable-long-${zoom === 1.5 ? 150 : 125}`);
  }
  scenario = "workspace-150";
  const id = await p.evaluate(
    (port) =>
      (window as any).forterm.connect({
        protocol: "sftp",
        host: "127.0.0.1",
        port,
        username: "ticket",
        password: "one-time@password",
        title: "控件可达性 · 回环服务",
        temporary: true,
      }),
    service.port,
  );
  const trust = p.getByRole("button", { name: "信任并连接", exact: true });
  await reachable(trust);
  await trust.click();
  await p.locator(".sftp-workspace .file-row").first().waitFor();
  // A pure SFTP workspace also needs a real scroll path to its file controls.
  const upload = p
    .locator(".sftp-workspace")
    .getByTitle("上传文件", { exact: true });
  await reachable(upload);
  const folder = p
    .locator(".sftp-workspace")
    .getByTitle("新建目录", { exact: true });
  await reachable(folder);
  await folder.click();
  await reachable(
    p
      .locator(".sftp-workspace .inline-editor")
      .getByRole("button", { name: "取消", exact: true }),
  );
  await p
    .locator(".sftp-workspace .inline-editor")
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await capture("reachable-sftp-150");
  const open = p.getByRole("button", { name: "在此会话打开终端", exact: true });
  await reachable(open);
  await open.click();
  await reachable(p.getByRole("button", { name: "同时显示", exact: true }));
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  const editFile = p
    .locator(
      ".workspace-files:not(.hidden) .file-source-view:not(.hidden) .file-row",
    )
    .filter({ hasText: "hello.txt" })
    .getByTitle("在线编辑", { exact: true });
  await reachable(editFile);
  await editFile.click();
  const text = p.getByLabel("远程文件内容", { exact: true });
  await reachable(text);
  await replaceCode(p, "可达性回归的显式保存");
  const save = p.getByRole("button", { name: "保存文件", exact: true });
  await reachable(save);
  await save.click();
  await p.getByText("文件已保存", { exact: true }).waitFor();
  assert.equal(
    await readFile(path.join(service.directory, "hello.txt"), "utf8"),
    "可达性回归的显式保存",
  );
  if (await p.getByRole("button", { name: "关闭提示", exact: true }).count())
    await p.getByRole("button", { name: "关闭提示", exact: true }).click();
  const ratio = p.getByRole("separator", {
    name: "监控面板宽度",
    exact: true,
  });
  await ratio.focus();
  await p.keyboard.press("End");
  const refresh = p.getByRole("button", { name: "刷新状态", exact: true });
  await reachable(refresh);
  await refresh.click();
  const auto = p.getByRole("checkbox", {
    name: "每 10 秒刷新（仅面板打开时）",
    exact: true,
  });
  await reachable(auto);
  await auto.check();
  await auto.uncheck();
  await ratio.focus();
  await p.keyboard.press("Home");
  await reachable(
    p
      .locator(".workspace-files:not(.hidden) .file-source-view:not(.hidden)")
      .getByTitle("上传文件", { exact: true }),
  );
  await reachable(refresh);
  await capture("reachable-workspace-150");
  // Exercise the worst-height combination: a full transfer drawer at 150%.
  const uploads = [];
  for (let i = 0; i < 6; i++) {
    const file = path.join(data, `queue-${i}.txt`);
    await writeFile(file, "synthetic queue data");
    uploads.push(file);
  }
  await app.evaluate(({ dialog }, files) => {
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: files,
    })) as typeof dialog.showOpenDialog;
  }, uploads);
  const queueUpload = p
    .locator(".workspace-files:not(.hidden) .file-source-view:not(.hidden)")
    .getByTitle("上传文件", { exact: true });
  await reachable(queueUpload);
  await queueUpload.click();
  await until(p, async () => {
    const jobs = (await (window as any).forterm.bootstrap()).transfers;
    return jobs.length === 6 && jobs.every((t: any) => t.state === "done");
  });
  await p.locator(".transfers summary").click();
  await reachable(refresh);
  await refresh.click();
  await reachable(text);
  await replaceCode(p, "展开队列时仍可显式保存");
  await reachable(save);
  await save.click();
  await p.getByText("文件已保存", { exact: true }).waitFor();
  if (await p.getByRole("button", { name: "关闭提示", exact: true }).count())
    await p.getByRole("button", { name: "关闭提示", exact: true }).click();
  await reachable(editFile);
  await capture("reachable-transfer-queue-150");
  const clear = p.getByRole("button", { name: "清理已结束记录", exact: true });
  await reachable(clear);
  await clear.click();
  await p.evaluate(() => (window as any).forterm.local());
  const split = p.getByTitle("终端分屏", { exact: true });
  await reachable(split);
  await split.click();
  for (const title of ["切换分屏方向", "重置布局", "切换文件面板"]) {
    const button = p.getByTitle(title, { exact: true });
    await reachable(button);
    await button.click();
  }
  await capture("reachable-split-toolbar-150");
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.webContents.setZoomFactor(1);
    w.setSize(1440, 940);
  });
  await p.waitForFunction(() => innerHeight > 800);
  scenario = "restored";
  await reachable(p.getByTitle("切换主题", { exact: true }));
  await reachable(sidebar.getByRole("button", { name: "导入", exact: true }));
  await reachable(sidebar.getByRole("button", { name: "导出", exact: true }));
  await capture("reachable-restored");
  assert.equal(service.received, "");
  assert.equal(service.authentications, 1);
  assert.deepEqual(errors, []);
  await p.evaluate((id) => (window as any).forterm.setDirty(id, false), id);
  console.log(
    "PASS: actual viewport/ancestor clipping and hit tests; mouse and keyboard import/export/theme/help/heart/settings; empty and 40+ saved connections at 125%/150%; pure SFTP toolbar and editor/monitor/split toolbar; native full-window screenshots; restored window; no shell stdin injection",
  );
} catch (error) {
  await capture("reachable-failure").catch(() => {});
  console.log(
    await app
      .windows()[0]
      ?.locator("body")
      .innerText()
      .catch(() => "unavailable"),
  );
  throw error;
} finally {
  await writeFile(
    `test-results/reachability-${version}.json`,
    JSON.stringify(
      {
        scenarios: [
          "empty-1.25",
          "empty-1.5",
          "long-1.25",
          "long-1.5",
          "workspace-150",
          "restored",
        ],
        measurements,
      },
      null,
      2,
    ),
  );
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
