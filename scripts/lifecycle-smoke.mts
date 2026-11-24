// Historical <= 0.1.6 UI scenario; the npm alias now runs three-column-smoke.mts.
import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture, waitFor } from "../tests/fixture";
import { ftpFixture } from "../tests/ftp-fixture";
import { until } from "./ui-helpers.mts";
const data = await mkdtemp(path.join(tmpdir(), "forterm-lifecycle-ui-")),
  f = await fixture(),
  ftp = await ftpFixture();
const text = Array.from(
  { length: 700 },
  (_, i) => `line${i}=value\t${i}  `,
).join("\r\n");
await writeFile(path.join(f.directory, "long.properties"), "\ufeff" + text);
await mkdir(path.join(f.directory, "dir"));
await writeFile(path.join(f.directory, "dir/inner.txt"), "inner");
const env = { ...process.env, FORTERM_TEST_DATA: path.join(data, "profile") };
delete env.ELECTRON_RUN_AS_NODE;
let app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
await mkdir("test-results", { recursive: true });
let stage = "startup";
const errors: string[] = [];
try {
  let p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  p.on("pageerror", (e) => errors.push(e.message));
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  let trusted = false;
  const connect = async (protocol: "ssh" | "sftp" | "ftp", title: string) => {
    const id = await p.evaluate((c) => (window as any).forterm.connect(c), {
      protocol,
      host: "127.0.0.1",
      port: protocol === "ftp" ? ftp.port : f.port,
      username: protocol === "ftp" ? "test" : "ticket",
      password:
        protocol === "ftp" ? "synthetic-ftp-password" : "one-time@password",
      temporary: true,
      title,
    });
    if (protocol !== "ftp" && !trusted) {
      await p.getByRole("button", { name: "信任并连接", exact: true }).click();
      trusted = true;
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
  const tab = async (id: string) =>
    p
      .locator(`.tab[data-session-id="${id}"]`)
      .locator("button")
      .first()
      .click();
  const source = await connect("sftp", "共享文件连接 A");
  await p
    .locator(".file-row")
    .filter({ hasText: "long.properties" })
    .getByRole("button", { name: "在线编辑", exact: true })
    .click();
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await editor.waitFor();
  await p.locator(".cm-scroller").hover();
  await p.mouse.wheel(0, 2800);
  await until(
    p,
    () => document.querySelector(".cm-scroller")!.scrollTop > 1000,
    undefined,
  );
  await p.locator(".cm-scroller").click({ position: { x: 90, y: 70 } });
  const initial = await editor.evaluate((el) => {
    const v = (el as any).cmTile.root.view;
    return {
      position: v.state.selection.main.head,
      text: v.state.doc.toString(),
      scroll: v.scrollDOM.scrollTop,
    };
  });
  stage = "dock state and actual separators";
  for (const dock of ["left", "bottom", "right"]) {
    await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption(dock);
    await until(
      p,
      ({ scroll }) =>
        Math.abs(document.querySelector(".cm-scroller")!.scrollTop - scroll) <
        3,
      { scroll: initial.scroll },
    );
    const current = await editor.evaluate((el) => {
      const v = (el as any).cmTile.root.view;
      return {
        position: v.state.selection.main.head,
        text: v.state.doc.toString(),
      };
    });
    assert.equal(current.position, initial.position);
    assert.equal(current.text, initial.text);
    const separator = p.getByRole("separator", {
      name: dock === "bottom" ? "编辑器高度" : "编辑器宽度",
      exact: true,
    });
    await separator.hover();
    const box = (await separator.boundingBox())!;
    await p.mouse.down();
    await p.mouse.move(
      box.x + (dock === "bottom" ? 0 : dock === "left" ? 30 : -30),
      box.y + (dock === "bottom" ? -30 : 0),
      { steps: 8 },
    );
    await p.mouse.up();
    await until(p, () => !document.body.dataset.resizing, undefined);
  }
  await p.getByTitle("收起编辑器", { exact: true }).click();
  await p.getByTitle("展开编辑器", { exact: true }).click();
  await until(
    p,
    ({ scroll }) =>
      Math.abs(document.querySelector(".cm-scroller")!.scrollTop - scroll) < 3,
    { scroll: initial.scroll },
  );
  assert.equal(
    await readFile(path.join(f.directory, "long.properties"), "utf8"),
    "\ufeff" + text,
  );
  const docState = await editor.evaluate(
    (el) => (el as any).cmTile.root.view.state.selection.main.head,
  );
  const a = await connect("ssh", "资产 A"),
    b = await connect("ssh", "资产 B 共用代理"),
    other = await connect("ftp", "独立 FTP");
  await tab(source);
  await editor.waitFor();
  assert.equal(
    await editor.evaluate(
      (el) => (el as any).cmTile.root.view.state.selection.main.head,
    ),
    docState,
  );
  await until(
    p,
    ({ scroll }) =>
      Math.abs(document.querySelector(".cm-scroller")!.scrollTop - scroll) < 3,
    { scroll: initial.scroll },
  );
  stage = "explicit links and shared connection lifetime";
  await p.evaluate(
    async ({ a, b, source }) => {
      await (window as any).forterm.linkFiles(a, source);
      await (window as any).forterm.linkFiles(b, source);
    },
    { a, b, source },
  );
  await app.evaluate(({ dialog }) => {
    (globalThis as any).confirmChoice = 0;
    (globalThis as any).dialogLog = [];
    dialog.showMessageBox = (async (_w: any, options: any) => {
      (globalThis as any).dialogLog.push(options);
      return {
        response: (globalThis as any).confirmChoice,
        checkboxChecked: false,
      };
    }) as typeof dialog.showMessageBox;
  });
  f.setBehavior({ writeDelayMs: 200 });
  const upload = path.join(data, "lifetime.bin");
  await writeFile(upload, Buffer.alloc(200000, 4));
  await app.evaluate(({ dialog }, file) => {
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: [file],
    })) as typeof dialog.showOpenDialog;
  }, upload);
  await p.evaluate((id) => (window as any).forterm.upload(id, "/"), source);
  await until(
    p,
    async () =>
      (await (window as any).forterm.bootstrap()).transfers.some((t: any) =>
        ["queued", "running"].includes(t.state),
      ),
    undefined,
  );
  assert.equal(
    await p.evaluate(
      async ({ b, other }) => (window as any).forterm.linkFiles(b, other),
      { b, other },
    ),
    false,
  );
  assert.equal(
    await p.evaluate(
      async (b) =>
        (await (window as any).forterm.bootstrap()).fileAssociations[b],
      b,
    ),
    source,
  );
  await app.evaluate(() => ((globalThis as any).confirmChoice = 1));
  assert.equal(
    await p.evaluate(
      async ({ b, other }) => (window as any).forterm.linkFiles(b, other),
      { b, other },
    ),
    true,
  );
  await until(
    p,
    async () =>
      (await (window as any).forterm.bootstrap()).transfers.every(
        (t: any) => !["queued", "running"].includes(t.state),
      ),
    undefined,
  );
  assert.equal(
    (await readFile(path.join(f.directory, "lifetime.bin"))).length,
    200000,
  );
  assert.equal(
    await p.evaluate(
      async (id) =>
        (await (window as any).forterm.bootstrap()).sessions.find(
          (s: any) => s.id === id,
        )?.status,
      source,
    ),
    "ready",
  );
  f.setBehavior({ writeDelayMs: 0 });
  await p.evaluate(
    async ({ b, source }) => (window as any).forterm.linkFiles(b, source),
    { b, source },
  );
  assert.equal(
    await p.evaluate(async (id) => (window as any).forterm.close(id), a),
    true,
  );
  assert.equal(
    await p.evaluate(
      async (id) =>
        (await (window as any).forterm.bootstrap()).sessions.find(
          (s: any) => s.id === id,
        )?.status,
      source,
    ),
    "ready",
  );
  assert.equal(
    await p.evaluate(
      async (b) =>
        (await (window as any).forterm.bootstrap()).fileAssociations[b],
      b,
    ),
    source,
  );
  stage = "context target, cancel delete and unsupported FTP permissions";
  await tab(b);
  await p.locator(".file-row").filter({ hasText: "hello.txt" }).waitFor();
  await app.evaluate(() => ((globalThis as any).confirmChoice = 0));
  const row = p.locator(".file-row").filter({ hasText: "hello.txt" });
  await row.click({ button: "right" });
  await p.getByRole("menuitem", { name: "删除文件", exact: true }).click();
  assert.equal(
    await readFile(path.join(f.directory, "hello.txt"), "utf8"),
    "hello 文件\n",
  );
  const log = await app.evaluate(() => (globalThis as any).dialogLog.at(-1));
  assert.ok(log.detail.includes(source) && log.detail.includes("/hello.txt"));
  await row.focus();
  await p.keyboard.press("Shift+F10");
  await p.getByRole("menu", { name: "操作菜单", exact: true }).waitFor();
  await p.keyboard.press("Escape");
  assert.equal(
    await p.getByRole("menu", { name: "操作菜单", exact: true }).count(),
    0,
  );
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(other);
  const ftpRow = p.locator(".file-row").filter({ hasText: "ftp.txt" });
  await ftpRow.waitFor();
  await ftpRow.click({ button: "right" });
  await p.getByRole("menuitem", { name: "权限", exact: true }).click();
  await p.getByText("权限未知，未显示为 000。", { exact: true }).waitFor();
  assert.equal(
    await p.getByRole("button", { name: "应用权限", exact: true }).isDisabled(),
    true,
  );
  await p.getByRole("button", { name: "关闭权限", exact: true }).click();
  assert.equal(ftp.verbs.filter((v) => v === "SITE").length, 0);
  stage = "high zoom menu and all dock positions";
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(source);
  await p
    .locator(".file-row")
    .filter({ hasText: "long.properties" })
    .getByRole("button", { name: "在线编辑", exact: true })
    .click();
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.setSize(1050, 650);
    w.webContents.setZoomFactor(1.5);
  });
  for (const dock of ["right", "left", "bottom"]) {
    await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption(dock);
    assert.ok(
      await editor.evaluate(
        (el) => (el as any).cmTile.root.view.state.doc.length > 1000,
      ),
    );
  }
  const selectedRow = p.locator(".file-row").filter({ hasText: "hello.txt" });
  await selectedRow.click({ button: "right" });
  const menu = p.getByRole("menu", { name: "操作菜单", exact: true });
  const r = (await menu.boundingBox())!;
  const viewport = await p.evaluate(() => ({
    width: innerWidth,
    height: innerHeight,
  }));
  assert.ok(
    r.x >= 0 &&
      r.y >= 0 &&
      r.x + r.width <= viewport.width + 1 &&
      r.y + r.height <= viewport.height + 1,
  );
  await p.evaluate(
    () =>
      new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      ),
  );
  const menuCapture = await app.evaluate(async ({ BrowserWindow }) =>
    (await BrowserWindow.getAllWindows()[0].capturePage())
      .toPNG()
      .toString("base64"),
  );
  await writeFile(
    "test-results/context-menu-high-zoom-0.1.5.png",
    Buffer.from(menuCapture, "base64"),
  );
  await p.keyboard.press("End");
  assert.equal(
    await p
      .getByRole("menuitem", { name: "删除文件", exact: true })
      .evaluate((el) => document.activeElement === el),
    true,
  );
  await p.keyboard.press("Escape");
  await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption("left");
  await until(
    p,
    () =>
      JSON.parse(localStorage.getItem("forterm.layout.v1") || "{}")
        .editorDock === "left",
    undefined,
  );
  await app.close();
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: ["."],
    env,
  });
  p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  assert.equal(
    await p.evaluate(
      () => JSON.parse(localStorage.getItem("forterm.layout.v1")!).editorDock,
    ),
    "left",
  );
  assert.deepEqual(errors, []);
  console.log(
    "PASS: long code selection/scroll/collapse/docks/real drag and cross-session cache; independent/shared file links survive workspace close; running transfer cancel/switch choice preserves original source; correct delete target and cancel; unsupported FTP mode is unknown not 000; keyboard context menus/edge/150%; dock preference restart",
  );
} catch (e) {
  await app
    .windows()[0]
    ?.screenshot({ path: "test-results/lifecycle-failure-0.1.5.png" })
    .catch(() => {});
  console.log("Lifecycle UI stage:", stage);
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
  await f.close();
  await ftp.close();
  await rm(data, { recursive: true, force: true });
}
