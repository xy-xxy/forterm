import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import path from "node:path";
import { tmpdir } from "node:os";
import assert from "node:assert/strict";
import { fixture } from "../tests/fixture";
import { until, replaceCode } from "./ui-helpers.mts";
const dir = await mkdtemp(path.join(tmpdir(), "forterm-navigation-")),
  f = await fixture();
await mkdir(path.join(f.directory, "sub"));
await writeFile(path.join(f.directory, "sub/keep.txt"), "original\n");
const env = { ...process.env, FORTERM_TEST_DATA: dir };
delete env.ELECTRON_RUN_AS_NODE;
const executable =
    process.env.FORTERM_PACKAGED_EXE || (electronPath as unknown as string),
  args = process.env.FORTERM_PACKAGED_EXE ? [] : ["."];
let app = await electron.launch({ executablePath: executable, args, env });
const version = JSON.parse(await readFile("package.json", "utf8")).version;
let stage = "startup";
try {
  let p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.show();
    w.focus();
  });
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const connect = async (protocol: "ssh" | "sftp") => {
    const id = await p.evaluate((c) => (window as any).forterm.connect(c), {
      protocol,
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
      title: protocol === "ssh" ? "导航验收终端" : "导航验收文件",
      temporary: true,
    });
    if (protocol === "ssh")
      await p.getByRole("button", { name: "信任并连接", exact: true }).click();
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
  const owner = await connect("ssh"),
    source = await connect("sftp");
  await p
    .locator(`.tab[data-session-id="${owner}"]`)
    .locator("button")
    .first()
    .click();
  await p
    .locator(".workspace-files:not(.hidden)")
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(source);
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  const view = p.locator(
    ".workspace-files:not(.hidden) .file-source-view:not(.hidden)",
  );
  await view.locator(".file-row").filter({ hasText: /^sub/ }).dblclick();
  await view.getByLabel("筛选文件", { exact: true }).fill("keep");
  await view
    .locator(".file-row")
    .filter({ hasText: /^keep.txt/ })
    .dblclick();
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await replaceCode(p, "draft survives navigation fold\n");
  await p.getByRole("button", { name: "刷新状态", exact: true }).click();
  await p.locator(".metric-grid").waitFor();
  const handle = p.getByRole("separator", { name: "连接栏宽度", exact: true });
  await handle.hover();
  const box = (await handle.boundingBox())!;
  await p.mouse.move(box.x + 3, box.y + 50);
  await p.mouse.down();
  await p.mouse.move(box.x + 33, box.y + 50, { steps: 8 });
  await p.mouse.up();
  const width = (await p.locator(".sidebar").boundingBox())!.width;
  const doc = await editor.evaluate((el: any) => ({
    text: el.cmTile.root.view.state.doc.toString(),
    head: el.cmTile.root.view.state.selection.main.head,
  }));
  const buffer = await p.evaluate(
      (id) => (window as any).forterm.attach(id),
      owner,
    ),
    exec = f.execRequests;
  stage = "Enter collapse and Space restore with focus and draft";
  await p.getByRole("button", { name: "收起连接栏", exact: true }).focus();
  await p.keyboard.press("Enter");
  await until(
    p,
    () =>
      document.activeElement?.classList.contains("navigation-reveal") ?? false,
  );
  assert.equal(await p.locator(".sidebar:not(.hidden)").count(), 0);
  assert.equal(
    await p
      .getByRole("button", { name: "展开连接栏", exact: true })
      .evaluate((el) => el.parentElement?.classList.contains("topbar")),
    true,
  );
  await p.keyboard.press("Space");
  await until(
    p,
    () =>
      document.activeElement?.classList.contains("sidebar-collapse") ?? false,
  );
  assert.ok(
    Math.abs((await p.locator(".sidebar").boundingBox())!.width - width) < 2,
  );
  assert.equal(
    await view.getByLabel("远程目录", { exact: true }).inputValue(),
    "/sub",
  );
  assert.equal(
    await view.getByLabel("筛选文件", { exact: true }).inputValue(),
    "keep",
  );
  assert.deepEqual(
    await editor.evaluate((el: any) => ({
      text: el.cmTile.root.view.state.doc.toString(),
      head: el.cmTile.root.view.state.selection.main.head,
    })),
    doc,
  );
  assert.equal(f.execRequests, exec);
  assert.equal(
    await p.evaluate((id) => (window as any).forterm.attach(id), owner),
    buffer,
  );
  assert.equal(f.received, "");
  assert.equal(
    await readFile(path.join(f.directory, "sub/keep.txt"), "utf8"),
    "original\n",
  );
  stage = "150 percent pinned restore control survives toolbar scroll";
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.setSize(1050, 650);
    w.webContents.setZoomFactor(1.5);
  });
  await p.getByRole("button", { name: "收起连接栏", exact: true }).click();
  await p.getByRole("button", { name: "同时显示", exact: true }).focus();
  const reveal = p.getByRole("button", { name: "展开连接栏", exact: true });
  await reveal.waitFor();
  const hit = await reveal.evaluate((el) => {
    const r = el.getBoundingClientRect(),
      point = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
    return {
      inside:
        r.x >= 0 &&
        r.y >= 0 &&
        r.right <= innerWidth &&
        r.bottom <= innerHeight,
      hit: !!point && el.contains(point),
    };
  });
  assert.deepEqual(hit, { inside: true, hit: true });
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
    `test-results/navigation-collapsed-${version}.png`,
    Buffer.from(png, "base64"),
  );
  await reveal.click();
  await p.getByRole("button", { name: "收起连接栏", exact: true }).waitFor();
  assert.equal(
    await view.getByLabel("筛选文件", { exact: true }).inputValue(),
    "keep",
  );
  assert.ok(
    (await p.locator(".editor-tabs .selected").innerText()).includes("未保存"),
  );
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.webContents.setZoomFactor(1);
    w.setSize(1440, 940);
  });
  await p.getByRole("button", { name: "收起连接栏", exact: true }).click();
  stage = "restart preserves hidden state and original width";
  await app.evaluate(({ dialog }) => {
    dialog.showMessageBox = (async () => ({
      response: 1,
      checkboxChecked: false,
    })) as typeof dialog.showMessageBox;
  });
  await app.close();
  app = await electron.launch({ executablePath: executable, args, env });
  p = await app.firstWindow();
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  assert.equal(await p.locator(".sidebar:not(.hidden)").count(), 0);
  await p.getByRole("button", { name: "展开连接栏", exact: true }).focus();
  await p.keyboard.press("Space");
  await until(p, () => !!document.querySelector(".sidebar:not(.hidden)"));
  assert.ok(
    Math.abs((await p.locator(".sidebar").boundingBox())!.width - width) < 2,
  );
  assert.equal(
    (
      await p.evaluate(
        async () => (await (window as any).forterm.bootstrap()).sessions,
      )
    ).length,
    0,
  );
  assert.equal(f.authentications, 2);
  assert.equal(
    await readFile(path.join(f.directory, "sub/keep.txt"), "utf8"),
    "original\n",
  );
  assert.deepEqual(errors, []);
  console.log(
    "PASS: sidebar-top collapse and pinned topbar restore; real Enter/Space and pointer actions, focus handoff, original width and restart visibility; 150% hit-test outside toolbar scrolling; file source/path/filter and dirty editor preserved, monitor/terminal/connection unaffected, no auto-save or reauthentication",
  );
} catch (e) {
  console.log("Navigation stage:", stage);
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
  await rm(dir, { recursive: true, force: true });
}
