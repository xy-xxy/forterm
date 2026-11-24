import { _electron as electron, type Page } from "playwright";
import electronPath from "electron";
import { mkdtemp, rm, writeFile, readFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { tmpdir } from "node:os";
import assert from "node:assert/strict";
import { fixture, waitFor } from "../tests/fixture";
import { ftpFixture } from "../tests/ftp-fixture";
import { until, replaceCode } from "./ui-helpers.mts";
const dir = await mkdtemp(path.join(tmpdir(), "forterm-three-columns-")),
  a = await fixture(),
  b = await fixture(),
  ftp = await ftpFixture();
await mkdir(path.join(a.directory, "adir"));
for (let i = 0; i < 120; i++)
  await writeFile(
    path.join(a.directory, "adir", `item-${String(i).padStart(3, "0")}.txt`),
    `A ${i}`,
  );
await writeFile(path.join(a.directory, "hello.txt"), "source A\n");
await writeFile(path.join(b.directory, "hello.txt"), "source B\n");
await writeFile(path.join(b.directory, "binary.bin"), Buffer.from([0, 1, 2]));
await writeFile(
  path.join(b.directory, "large.log"),
  "x".repeat(1024 * 1024 + 1),
);
await writeFile(
  path.join(a.directory, "long.properties"),
  Array.from({ length: 600 }, (_, i) => `key${i}=value${i}`).join("\n"),
);
const env = { ...process.env, FORTERM_TEST_DATA: path.join(dir, "profile") };
delete env.ELECTRON_RUN_AS_NODE;
const executable =
    process.env.FORTERM_PACKAGED_EXE || (electronPath as unknown as string),
  appArgs = process.env.FORTERM_PACKAGED_EXE ? [] : ["."];
let app = await electron.launch({
  executablePath: executable,
  args: appArgs,
  env,
});
const version = JSON.parse(await readFile("package.json", "utf8")).version;
let stage = "startup";
async function capture(p: Page, name: string) {
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
    `test-results/${name}-${version}.png`,
    Buffer.from(image, "base64"),
  );
}
async function drag(p: Page, name: string, dx: number, dy = 0) {
  const handle = p.getByRole("separator", { name, exact: true });
  await handle.hover();
  const box = (await handle.boundingBox())!;
  await p.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await p.mouse.down();
  assert.equal(await handle.getAttribute("data-dragging"), "true");
  await p.mouse.move(box.x + box.width / 2 + dx, box.y + box.height / 2 + dy, {
    steps: 8,
  });
  await p.mouse.up();
  assert.equal(
    await p.evaluate(() => document.body.dataset.resizing),
    undefined,
  );
  return Number(await handle.getAttribute("aria-valuenow"));
}
try {
  let p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await mkdir("test-results", { recursive: true });
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  await p.addInitScript(() => {
    localStorage.setItem(
      "forterm.layout.v1",
      JSON.stringify({
        sidebar: 220,
        panel: 300,
        editorDock: "right",
        editorWidth: 300,
        editor: 180,
      }),
    );
  });
  await p.reload();
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  await until(
    p,
    () =>
      JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}")
        .filesWidth === 300,
  );
  assert.equal(
    Math.round((await p.locator(".sidebar").boundingBox())!.width),
    220,
  );
  let trustedA = false,
    trustedB = false;
  const connect = async (
    protocol: "ssh" | "sftp" | "ftp",
    title: string,
    port: number,
  ) => {
    const id = await p.evaluate((c) => (window as any).forterm.connect(c), {
      protocol,
      title,
      host: "127.0.0.1",
      port,
      username: protocol === "ftp" ? "test" : "ticket",
      password:
        protocol === "ftp" ? "synthetic-ftp-password" : "one-time@password",
      temporary: true,
    });
    if (protocol !== "ftp" && !(port === a.port ? trustedA : trustedB)) {
      await p.getByRole("button", { name: "信任并连接", exact: true }).click();
      if (port === a.port) trustedA = true;
      else trustedB = true;
    }
    await until(
      p,
      async (id) =>
        (await (window as any).forterm.bootstrap()).sessions.find(
          (s: any) => s.id === id,
        )?.status === "ready",
      id,
    );
    return id;
  };
  const owner = await connect("ssh", "资产终端", a.port),
    sourceA = await connect("sftp", "文件 A", a.port),
    sourceB = await connect("sftp", "文件 B", b.port),
    sourceFtp = await connect("ftp", "文件 FTP", ftp.port);
  const topTab = (id: string) => p.locator(`.tab[data-session-id="${id}"]`);
  const selectOwner = async (id: string) => {
    await topTab(id).locator("button").first().click();
    await until(
      p,
      (id) =>
        document
          .querySelector(".tab.selected")
          ?.getAttribute("data-session-id") === id,
      id,
    );
  };
  await selectOwner(owner);
  const workspace = () =>
      p.locator(`.workspace-files[data-workspace-id="${owner}"]`),
    fileView = () => workspace().locator(".file-source-view:not(.hidden)");
  const fileTab = (id: string) =>
    workspace().locator(
      `.file-source-tag[data-file-source-id="${id}"] [role="tab"]`,
    );
  const switchSource = async (id: string) => {
    await fileTab(id).click();
    await until(
      p,
      ({ owner, id }) =>
        document
          .querySelector(
            `.workspace-files[data-workspace-id="${owner}"] .file-source-view:not(.hidden)`,
          )
          ?.getAttribute("data-file-source-id") === id,
      { owner, id },
    );
  };
  const row = (name: string) =>
    fileView()
      .locator(".file-row")
      .filter({
        hasText: new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`),
      });
  stage = "two explicit source tabs and three columns";
  assert.equal(await topTab(sourceA).count(), 1);
  assert.equal(await topTab(sourceB).count(), 1);
  await workspace()
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(sourceA);
  await workspace()
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(sourceB);
  assert.equal(await topTab(sourceA).count(), 0);
  assert.equal(await topTab(sourceB).count(), 0);
  assert.equal(await fileTab(sourceA).count(), 1);
  assert.equal(await fileTab(sourceB).count(), 1);
  await switchSource(sourceA);
  await app.evaluate(
    ({ BrowserWindow }, id) =>
      BrowserWindow.getAllWindows()[0].webContents.send("forterm:event", {
        type: "session-focus",
        id,
      }),
    sourceB,
  );
  await until(
    p,
    ({ owner, sourceB }) =>
      document
        .querySelector(".tab.selected")
        ?.getAttribute("data-session-id") === owner &&
      document
        .querySelector(
          `.workspace-files[data-workspace-id="${owner}"] .file-source-view:not(.hidden)`,
        )
        ?.getAttribute("data-file-source-id") === sourceB,
    { owner, sourceB },
  );
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  const positions = await p.evaluate(() => {
    const file = document
        .querySelector('[aria-label="左侧文件区"]')!
        .getBoundingClientRect(),
      center = document
        .querySelector(".workspace-stack")!
        .getBoundingClientRect(),
      monitor = document
        .querySelector('[aria-label="右侧监控区"]')!
        .getBoundingClientRect();
    return {
      file: { x: file.x, right: file.right, w: file.width },
      center: { x: center.x, right: center.right, w: center.width },
      monitor: { x: monitor.x, right: monitor.right, w: monitor.width },
    };
  });
  assert.ok(
    positions.file.w > 0 && positions.center.w > 0 && positions.monitor.w > 0,
  );
  assert.ok(
    positions.file.right <= positions.center.x + 1 &&
      positions.center.right <= positions.monitor.x + 1,
  );
  await p.getByRole("button", { name: "刷新状态", exact: true }).click();
  await p.locator(".metric-grid").waitFor();
  assert.equal(b.execRequests, 0);
  assert.equal(a.execRequests, 1);
  await fileTab(sourceA).focus();
  await p.keyboard.press("End");
  await until(
    p,
    ({ owner, sourceB }) =>
      (
        document.querySelector(
          `.workspace-files[data-workspace-id="${owner}"] .file-source-view:not(.hidden)`,
        ) as HTMLElement
      )?.dataset.fileSourceId === sourceB,
    { owner, sourceB },
  );
  await p.keyboard.press("Home");
  await until(
    p,
    (owner) =>
      (
        document.querySelector(
          `.workspace-files[data-workspace-id="${owner}"] .file-source-view:not(.hidden)`,
        ) as HTMLElement
      )?.dataset.fileSourceId === owner,
    owner,
  );
  await p.keyboard.press("ArrowRight");
  await until(
    p,
    ({ owner, sourceA }) =>
      (
        document.querySelector(
          `.workspace-files[data-workspace-id="${owner}"] .file-source-view:not(.hidden)`,
        ) as HTMLElement
      )?.dataset.fileSourceId === sourceA,
    { owner, sourceA },
  );
  stage = "per-source directory filter sort and scroll";
  await switchSource(sourceA);
  await row("adir").dblclick();
  await fileView().getByLabel("筛选文件", { exact: true }).fill("item");
  await fileView()
    .getByLabel("文件排序", { exact: true })
    .selectOption("modified");
  await fileView().getByLabel("远程文件列表", { exact: true }).hover();
  await p.mouse.wheel(0, 1400);
  await until(
    p,
    ({ owner }) =>
      document.querySelector(
        `.workspace-files[data-workspace-id="${owner}"] .file-source-view:not(.hidden) .file-list`,
      )!.scrollTop > 300,
    { owner },
  );
  const scroll = await fileView()
    .locator(".file-list")
    .evaluate((el) => el.scrollTop);
  await switchSource(sourceB);
  await row("hello.txt").waitFor();
  await fileView().getByLabel("筛选文件", { exact: true }).fill("hello");
  await fileView().getByLabel("文件排序", { exact: true }).selectOption("size");
  await switchSource(sourceA);
  assert.equal(
    await fileView().getByLabel("远程目录", { exact: true }).inputValue(),
    "/adir",
  );
  assert.equal(
    await fileView().getByLabel("筛选文件", { exact: true }).inputValue(),
    "item",
  );
  assert.equal(
    await fileView().getByLabel("文件排序", { exact: true }).inputValue(),
    "modified",
  );
  assert.ok(
    Math.abs(
      (await fileView()
        .locator(".file-list")
        .evaluate((el) => el.scrollTop)) - scroll,
    ) < 3,
  );
  await fileView().getByLabel("筛选文件", { exact: true }).fill("");
  await fileView().getByTitle("上级目录", { exact: true }).click();
  await row("hello.txt").waitFor();
  stage =
    "double click editor stays inside terminal, duplicate focus and independent saves";
  const reads = a.readOpenRequests;
  await row("hello.txt").dblclick();
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await editor.waitFor();
  const after = a.readOpenRequests;
  assert.ok(after > reads);
  await row("hello.txt").dblclick();
  assert.equal(a.readOpenRequests, after);
  assert.equal(await p.locator(".editor-tabs [data-document-path]").count(), 1);
  assert.equal(await app.windows().length, 1);
  assert.equal(
    (
      await p.evaluate(
        async () => (await (window as any).forterm.bootstrap()).sessions,
      )
    ).length,
    4,
  );
  assert.ok(
    (await p.locator(".terminal-view:not(.hidden)").boundingBox())!.width > 100,
  );
  await replaceCode(p, "draft A\n");
  await switchSource(sourceB);
  assert.ok((await editor.innerText()).includes("draft A"));
  await fileView().getByLabel("筛选文件", { exact: true }).fill("");
  await row("hello.txt").dblclick();
  await until(
    p,
    () =>
      document
        .querySelector(".editor-tabs .selected")
        ?.getAttribute("data-file-source-id") !== null &&
      document
        .querySelector(".editor-tabs .selected")
        ?.getAttribute("data-file-source-id") ===
        document
          .querySelector(
            ".workspace-files:not(.hidden) .file-source-view:not(.hidden)",
          )
          ?.getAttribute("data-file-source-id"),
  );
  await replaceCode(p, "draft B\n");
  assert.equal(await p.locator(".editor-tabs [data-document-path]").count(), 2);
  const docTab = (id: string) =>
    p.locator(
      `.editor-tabs [data-file-source-id="${id}"][data-document-path="/hello.txt"]`,
    );
  await docTab(sourceA).click();
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !document
        .querySelector(".editor-tabs .selected")
        ?.textContent?.includes("未保存"),
  );
  assert.equal(
    await readFile(path.join(a.directory, "hello.txt"), "utf8"),
    "draft A\n",
  );
  assert.equal(
    await readFile(path.join(b.directory, "hello.txt"), "utf8"),
    "source B\n",
  );
  await docTab(sourceB).click();
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !document
        .querySelector(".editor-tabs .selected")
        ?.textContent?.includes("未保存"),
  );
  assert.equal(
    await readFile(path.join(b.directory, "hello.txt"), "utf8"),
    "draft B\n",
  );
  const terminal = p.locator(
    `[data-session-id="${owner}"] .xterm-helper-textarea`,
  );
  await terminal.focus();
  await p.keyboard.insertText("terminal-still-editable");
  await waitFor(() => a.received.includes("terminal-still-editable"));
  await capture(p, "three-columns");
  stage = "unsupported double click never downloads or opens a fake editor";
  const docs = await p.locator(".editor-tabs [data-document-path]").count();
  await row("binary.bin").dblclick();
  await p
    .getByRole("alert")
    .filter({ hasText: /二进制|UTF/ })
    .waitFor();
  await row("large.log").dblclick();
  await p
    .getByRole("alert")
    .filter({ hasText: /1 MiB|过大|超过/ })
    .waitFor();
  assert.equal(
    await p.locator(".editor-tabs [data-document-path]").count(),
    docs,
  );
  await app.evaluate(({ dialog }) => {
    (globalThis as any).downloads = 0;
    dialog.showSaveDialog = (async () => {
      (globalThis as any).downloads++;
      return { canceled: true };
    }) as typeof dialog.showSaveDialog;
  });
  await workspace()
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(sourceFtp);
  await row("ftp.txt").dblclick();
  await p.getByRole("alert").filter({ hasText: "FTP 暂不支持" }).waitFor();
  assert.equal(await app.evaluate(() => (globalThis as any).downloads), 0);
  assert.equal(ftp.verbs.filter((v) => v === "RETR").length, 0);
  stage = "separate pane drag, docking, collapse, keyboard and cancel";
  await switchSource(sourceA);
  await row("long.properties").dblclick();
  await editor.waitFor();
  await p.locator(".cm-scroller").hover();
  await p.mouse.wheel(0, 1800);
  await until(p, () => document.querySelector(".cm-scroller")!.scrollTop > 500);
  await editor.click({ position: { x: 60, y: 70 } });
  const state = await editor.evaluate((el: any) => ({
    head: el.cmTile.root.view.state.selection.main.head,
    scroll: el.cmTile.root.view.scrollDOM.scrollTop,
  }));
  await p.getByRole("button", { name: /^(收起连接栏|展开连接栏)$/ }).click();
  await p.getByRole("button", { name: /^(收起连接栏|展开连接栏)$/ }).click();
  await editor.focus();
  await p.keyboard.insertText("# layout draft");
  assert.ok(
    (await p.locator(".editor-tabs .selected").innerText()).includes("未保存"),
  );
  const draftText = await editor.evaluate((el: any) =>
    el.cmTile.root.view.state.doc.toString(),
  );
  state.head = await editor.evaluate(
    (el: any) => el.cmTile.root.view.state.selection.main.head,
  );
  const beforeResize = a.resizes.length;
  await drag(p, "文件面板宽度", -25);
  await drag(p, "监控面板宽度", -20);
  await waitFor(() => a.resizes.length > beforeResize);
  for (const dock of ["left", "bottom", "right"]) {
    await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption(dock);
    assert.equal(
      await editor.evaluate(
        (el: any) => el.cmTile.root.view.state.selection.main.head,
      ),
      state.head,
    );
  }
  await p.getByRole("button", { name: "收起文件区", exact: true }).click();
  assert.equal(
    await p
      .getByRole("separator", { name: "文件面板宽度", exact: true })
      .count(),
    0,
  );
  await p.getByRole("button", { name: "切换文件面板", exact: true }).click();
  await p.getByRole("button", { name: "收起监控区", exact: true }).click();
  assert.equal(
    await p
      .getByRole("separator", { name: "监控面板宽度", exact: true })
      .count(),
    0,
  );
  await p.getByRole("button", { name: "切换监控面板", exact: true }).click();
  const handle = p.getByRole("separator", {
    name: "文件面板宽度",
    exact: true,
  });
  await handle.focus();
  await p.keyboard.press("ArrowRight");
  await p.keyboard.press("Enter");
  await handle.evaluate((el) =>
    el.addEventListener(
      "pointerdown",
      (event) => {
        (window as any).actualPointer = (event as PointerEvent).pointerId;
      },
      { once: true },
    ),
  );
  await handle.hover();
  const cancelBox = (await handle.boundingBox())!;
  await p.mouse.move(cancelBox.x + cancelBox.width / 2, cancelBox.y + 20);
  await p.mouse.down();
  assert.equal(await handle.getAttribute("data-dragging"), "true");
  await handle.evaluate((el) =>
    el.dispatchEvent(
      new PointerEvent("pointercancel", {
        pointerId: (window as any).actualPointer,
        isPrimary: true,
        bubbles: true,
      }),
    ),
  );
  await p.mouse.up();
  assert.equal(
    await p.evaluate(() => document.body.dataset.resizing),
    undefined,
  );
  await drag(p, "文件面板宽度", -10);
  await p.getByTitle("重置布局", { exact: true }).click();
  assert.equal(
    await editor.evaluate((el: any) =>
      el.cmTile.root.view.state.doc.toString(),
    ),
    draftText,
  );
  assert.equal(
    (
      await readFile(path.join(a.directory, "long.properties"), "utf8")
    ).includes("# layout draft"),
    false,
  );
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  stage = "dirty unlink cancel and retained source-bound documents";
  await docTab(sourceA).click();
  await replaceCode(p, "retained after unlink\n");
  await app.evaluate(({ dialog }) => {
    (globalThis as any).confirm = 0;
    (globalThis as any).lastQuestion = null;
    dialog.showMessageBox = (async (_w: any, options: any) => {
      (globalThis as any).lastQuestion = options;
      return { response: (globalThis as any).confirm, checkboxChecked: false };
    }) as typeof dialog.showMessageBox;
  });
  await workspace()
    .getByRole("button", { name: "解除文件关联 文件 A", exact: true })
    .click();
  assert.equal(await fileTab(sourceA).count(), 1);
  await app.evaluate(() => ((globalThis as any).confirm = 1));
  await workspace()
    .getByRole("button", { name: "解除文件关联 文件 A", exact: true })
    .click();
  await until(
    p,
    ({ owner, sourceA }) =>
      !document.querySelector(
        `.workspace-files[data-workspace-id="${owner}"] .file-source-tag[data-file-source-id="${sourceA}"]`,
      ),
    { owner, sourceA },
  );
  assert.ok((await editor.innerText()).includes("retained after unlink"));
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !document
        .querySelector(".editor-tabs .selected")
        ?.textContent?.includes("未保存"),
  );
  assert.equal(
    await readFile(path.join(a.directory, "hello.txt"), "utf8"),
    "retained after unlink\n",
  );
  assert.equal(await topTab(sourceA).count(), 1);
  await workspace()
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(sourceA);
  stage =
    "disconnected source stays selectable with correct document capability";
  b.dropConnections();
  await until(
    p,
    async (id) =>
      ["closed", "error"].includes(
        (await (window as any).forterm.bootstrap()).sessions.find(
          (s: any) => s.id === id,
        )?.status,
      ),
    sourceB,
  );
  await switchSource(sourceB);
  await docTab(sourceB).click();
  await replaceCode(p, "offline draft B");
  assert.equal(
    await p.getByRole("button", { name: "保存文件", exact: true }).isDisabled(),
    true,
  );
  await docTab(sourceA).click();
  await replaceCode(p, "ready source A draft\n");
  assert.equal(
    await p.getByRole("button", { name: "保存文件", exact: true }).isEnabled(),
    true,
  );
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !document
        .querySelector(".editor-tabs .selected")
        ?.textContent?.includes("未保存"),
  );
  assert.equal(
    await readFile(path.join(b.directory, "hello.txt"), "utf8"),
    "draft B\n",
  );
  stage = "shared references, running transfer and workspace close";
  const secondOwner = await connect("ssh", "共享终端", a.port);
  await p
    .locator(`.workspace-files[data-workspace-id="${secondOwner}"]`)
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(sourceA);
  await selectOwner(owner);
  stage = "two-terminal split keeps keyboard input on the focused session";
  await p.getByTitle("终端分屏", { exact: true }).click();
  const splitHandle = p.getByRole("separator", {
    name: "双终端分屏比例",
    exact: true,
  });
  await splitHandle.waitFor();
  await drag(p, "双终端分屏比例", 0, 20);
  await p
    .locator(`[data-session-id="${secondOwner}"] .xterm-helper-textarea`)
    .focus();
  await p.keyboard.insertText("second-pane-input");
  await waitFor(() => a.shellInputs[1]?.includes("second-pane-input"));
  assert.equal(a.shellInputs[0].includes("second-pane-input"), false);
  await selectOwner(owner);
  await p.getByTitle("切换分屏方向", { exact: true }).click();
  await drag(p, "双终端分屏比例", 20, 0);
  await p.getByTitle("终端分屏", { exact: true }).click();
  const upload = path.join(dir, "queue.bin");
  await writeFile(upload, Buffer.alloc(256000, 7));
  a.setBehavior({ writeDelayMs: 80 });
  await app.evaluate(({ dialog }, file) => {
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: [file],
    })) as typeof dialog.showOpenDialog;
  }, upload);
  await p.evaluate((id) => (window as any).forterm.upload(id, "/"), sourceA);
  await until(p, async () =>
    (await (window as any).forterm.bootstrap()).transfers.some(
      (t: any) => t.state === "running",
    ),
  );
  await switchSource(sourceB);
  await app.evaluate(() => ((globalThis as any).confirm = 0));
  await workspace()
    .getByRole("button", { name: "解除文件关联 文件 A", exact: true })
    .click();
  assert.equal(await fileTab(sourceA).count(), 1);
  assert.ok(
    (
      await app.evaluate(() => (globalThis as any).lastQuestion)
    ).detail.includes("传输继续"),
  );
  await app.evaluate(() => ((globalThis as any).confirm = 1));
  await workspace()
    .getByRole("button", { name: "解除文件关联 文件 A", exact: true })
    .click();
  await until(
    p,
    ({ owner, sourceA }) =>
      !document.querySelector(
        `.workspace-files[data-workspace-id="${owner}"] .file-source-tag[data-file-source-id="${sourceA}"]`,
      ),
    { owner, sourceA },
  );
  await topTab(owner)
    .getByRole("button", { name: "关闭会话", exact: true })
    .click();
  await until(
    p,
    (id) => !document.querySelector(`.tab[data-session-id="${id}"]`),
    owner,
  );
  assert.equal(await topTab(sourceA).count(), 0);
  await until(p, async () =>
    (await (window as any).forterm.bootstrap()).transfers.every(
      (t: any) => !["queued", "running"].includes(t.state),
    ),
  );
  assert.equal(
    (await readFile(path.join(a.directory, "queue.bin"))).length,
    256000,
  );
  await assert.rejects(() => readFile(path.join(b.directory, "queue.bin")));
  a.setBehavior({ writeDelayMs: 0 });
  stage = "150 percent layout bounds and persistence";
  await selectOwner(secondOwner);
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  await p
    .locator(
      `.workspace-files[data-workspace-id="${secondOwner}"] .file-source-view:not(.hidden) .file-row`,
    )
    .filter({ hasText: /^hello.txt/ })
    .dblclick();
  await editor.waitFor();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.setSize(1050, 650);
    w.webContents.setZoomFactor(1.5);
  });
  await until(p, () => {
    const center = document
        .querySelector(".workspace-stack")!
        .getBoundingClientRect(),
      monitor = document
        .querySelector(".workspace-monitor-pane")!
        .getBoundingClientRect();
    return (
      innerWidth <= 700 &&
      innerWidth >= 650 &&
      center.width > 100 &&
      monitor.right <= innerWidth + 1
    );
  });
  const measure = await p.evaluate(() => {
    const elements = [
      ...document.querySelectorAll<HTMLElement>(
        ".workspace-files-pane,.workspace-stack,.workspace-monitor-pane,.terminal-view:not(.hidden),.editor-region",
      ),
    ].filter((el) => el.getClientRects().length);
    return {
      w: innerWidth,
      h: innerHeight,
      rootScroll: document.querySelector(".app")!.scrollTop,
      items: elements.map((el) => {
        const r = el.getBoundingClientRect();
        return {
          c: el.className,
          x: r.x,
          y: r.y,
          w: r.width,
          h: r.height,
          right: r.right,
          bottom: r.bottom,
        };
      }),
    };
  });
  assert.equal(measure.rootScroll, 0);
  assert.ok(
    measure.items.every(
      (r) =>
        r.w > 0 &&
        r.h > 0 &&
        r.x >= -1 &&
        r.right <= measure.w + 1 &&
        r.bottom <= measure.h + 1,
    ),
    JSON.stringify(measure),
  );
  assert.equal(
    await p
      .locator(".workspace-stack.editor-dock-bottom:not(.no-editor)")
      .count(),
    1,
  );
  await p
    .locator(`[data-session-id="${secondOwner}"] .xterm-helper-textarea`)
    .focus();
  await p.keyboard.insertText("narrow-terminal-input");
  await waitFor(() => a.received.includes("narrow-terminal-input"));
  await p.getByRole("button", { name: "刷新状态", exact: true }).click();
  await until(
    p,
    () =>
      !(
        document.querySelector(
          '.workspace-monitor-view:not(.hidden) [aria-label="刷新状态"]',
        ) as HTMLButtonElement
      )?.disabled,
  );
  const auto = p.getByRole("checkbox", {
      name: "每 10 秒刷新（仅面板打开时）",
      exact: true,
    }),
    beforeAuto = a.execRequests;
  await auto.check();
  await waitFor(() => a.execRequests > beforeAuto);
  await until(
    p,
    () =>
      !(
        document.querySelector(
          '.workspace-monitor-view:not(.hidden) [aria-label="刷新状态"]',
        ) as HTMLButtonElement
      )?.disabled,
  );
  await p.getByRole("button", { name: "切换监控面板", exact: true }).click();
  const whileHidden = a.execRequests;
  await new Promise((r) => setTimeout(r, 10300));
  assert.equal(a.execRequests, whileHidden);
  await p.getByRole("button", { name: "切换监控面板", exact: true }).click();
  await waitFor(() => a.execRequests > whileHidden);
  await auto.uncheck();
  await capture(p, "three-columns-150");
  await p.getByRole("button", { name: /^(收起连接栏|展开连接栏)$/ }).click();
  assert.equal(await p.locator(".sidebar:not(.hidden)").count(), 0);
  await p.getByRole("button", { name: /^(收起连接栏|展开连接栏)$/ }).click();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.webContents.setZoomFactor(1);
    w.setSize(1440, 940);
  });
  await p.getByRole("separator", { name: "文件面板宽度", exact: true }).focus();
  await p.keyboard.press("ArrowRight");
  await p.getByRole("separator", { name: "监控面板宽度", exact: true }).focus();
  await p.keyboard.press("ArrowLeft");
  await topTab(secondOwner)
    .getByRole("button", { name: "关闭会话", exact: true })
    .click();
  await topTab(sourceA).waitFor();
  assert.equal(
    await p.evaluate(
      async (id) =>
        (await (window as any).forterm.bootstrap()).sessions.find(
          (s: any) => s.id === id,
        )?.status,
      sourceA,
    ),
    "ready",
  );
  await p.getByRole("button", { name: /^(收起连接栏|展开连接栏)$/ }).click();
  await until(
    p,
    () =>
      JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}")
        .navigationVisible === false,
  );
  const preference = await p.evaluate(() =>
    JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}"),
  );
  await app.close();
  app = await electron.launch({
    executablePath: executable,
    args: appArgs,
    env,
  });
  p = await app.firstWindow();
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const persisted = await p.evaluate(() =>
    JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}"),
  );
  assert.equal(persisted.navigationVisible, false);
  assert.equal(persisted.filesVisible, true);
  assert.equal(persisted.monitorVisible, true);
  assert.equal(persisted.filesWidth, preference.filesWidth);
  assert.equal(persisted.monitorWidth, preference.monitorWidth);
  assert.equal(a.authentications, 3);
  assert.equal(b.authentications, 1);
  assert.deepEqual(errors, []);
  console.log(
    "PASS: left files + central terminal/editor + right monitor; explicit multi-source tabs preserve directory/filter/sort/scroll; dirty multi-source documents and original-source saves; double-click directory/text/dedup/unsupported; independent resize/collapse/keyboard/cancel/live PTY; unlink confirmation/shared refs/running upload; 150% bounds; migrated and restarted preferences; no reauthentication",
  );
} catch (e) {
  console.log("Three-column stage:", stage);
  await app
    .windows()[0]
    ?.screenshot({ path: `test-results/three-columns-failure-${version}.png` })
    .catch(() => {});
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
  await a.close();
  await b.close();
  await ftp.close();
  await rm(dir, { recursive: true, force: true });
}
