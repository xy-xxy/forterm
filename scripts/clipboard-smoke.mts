import { _electron as electron, type Page } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture, waitFor } from "../tests/fixture";
import { until } from "./ui-helpers.mts";
const version = JSON.parse(await readFile("package.json", "utf8")).version;
const dir = await mkdtemp(path.join(tmpdir(), "forterm-clipboard-ui-")),
  f = await fixture();
await writeFile(
  path.join(f.directory, "format.yml"),
  "\ufeffbefore: true\r\nlast  ",
);
const env = { ...process.env, FORTERM_TEST_DATA: path.join(dir, "profile") };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
await mkdir("test-results", { recursive: true });
const mod = process.platform === "darwin" ? "Meta" : "Control";
let stage = "startup";
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
  const make = async (title: string, trust = false) => {
    const id = await p.evaluate((c) => (window as any).forterm.connect(c), {
      protocol: "ssh",
      host: "127.0.0.1",
      port: f.port,
      username: "ticket",
      password: "one-time@password",
      title,
      temporary: true,
    });
    if (trust)
      await p.getByRole("button", { name: "信任并连接", exact: true }).click();
    await until(
      p,
      async (id) =>
        (await (window as any).forterm.bootstrap()).sessions.find(
          (s: any) => s.id === id,
        )?.terminalStatus === "ready",
      id,
    );
    return id;
  };
  const a = await make("剪贴板资产 A", true),
    b = await make("剪贴板资产 B");
  const tab = async (id: string) => {
    await p.evaluate(async (id) => {
      const data = await (window as any).forterm.bootstrap(),
        index = data.sessions.findIndex((s: any) => s.id === id);
      (
        document
          .querySelectorAll(".tab")
          [index].querySelector("button") as HTMLElement
      ).click();
    }, id);
  };
  await tab(a);
  const terminal = p.locator(`[data-session-id="${a}"]`),
    focus = async () => terminal.locator(".xterm-helper-textarea").focus();
  await app.evaluate(({ ipcMain }) => {
    (globalThis as any).protocolInputs = [];
    ipcMain.on("forterm:input", (_e, id, text) =>
      (globalThis as any).protocolInputs.push({
        id,
        bytes: Buffer.byteLength(text),
      }),
    );
  });
  const count = () =>
    app.evaluate(() => (globalThis as any).protocolInputs.length);
  const clip = async (text: string) => {
    await app.evaluate(
      async ({ clipboard }, text) => clipboard.writeText(text),
      text,
    );
  };
  stage = "copy selection and Ctrl-C";
  await clip("KEEP_EXISTING");
  await focus();
  await p.keyboard.press("Control+Shift+c");
  assert.ok(
    await app.evaluate(
      async ({ clipboard }) => (await clipboard.readText()) === "KEEP_EXISTING",
    ),
  );
  assert.equal(await count(), 0);
  await p.keyboard.press("Control+c");
  await waitFor(() => f.shellInputs[0] === "\x03");
  assert.equal(f.shellInputs[1], "");
  await terminal.locator(".terminal-canvas").click({ button: "right" });
  await p.getByRole("menuitem", { name: "全选", exact: true }).click();
  await focus();
  const copyBefore = await count();
  await p.keyboard.press("Control+Shift+c");
  await until(p, () => true, undefined);
  assert.ok(
    await app.evaluate(async ({ clipboard }) =>
      (await clipboard.readText()).includes("你好"),
    ),
  );
  await p.keyboard.press("Control+c");
  assert.equal(await count(), copyBefore);
  stage = "keyboard paste paths";
  for (const key of [
    "Control+Shift+v",
    "Control+v",
    "Shift+Insert",
    ...(process.platform === "darwin" ? ["Meta+v"] : []),
  ]) {
    const text = `键盘${key}😀\tX`;
    await clip(text);
    await focus();
    const before = f.shellInputs[0],
      events = await count();
    await p.keyboard.press(key);
    await waitFor(() => f.shellInputs[0] === before + text);
    assert.equal(await count(), events + 1);
    assert.equal(f.shellInputs[1], "");
  }
  stage = "right-menu paste";
  await clip("右键中文😀\tY");
  const menuBefore = f.shellInputs[0],
    menuCount = await count();
  await terminal.locator(".terminal-canvas").click({ button: "right" });
  await p.getByRole("menuitem", { name: "粘贴", exact: true }).click();
  await waitFor(() => f.shellInputs[0] === menuBefore + "右键中文😀\tY");
  assert.equal(await count(), menuCount + 1);
  stage = "native paste and multiline";
  const multiline = "第一行\r\n第二行\n";
  await clip(multiline);
  await focus();
  const lineBefore = f.shellInputs[0],
    lineCount = await count();
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.paste(),
  );
  await p.getByRole("dialog", { name: "确认终端粘贴", exact: true }).waitFor();
  await p.getByRole("button", { name: "取消粘贴", exact: true }).click();
  assert.equal(f.shellInputs[0], lineBefore);
  assert.equal(await count(), lineCount);
  await focus();
  await p.keyboard.press("Control+v");
  await p.getByRole("button", { name: "确认粘贴", exact: true }).click();
  await waitFor(
    () => f.shellInputs[0] === lineBefore + multiline.replace(/\r?\n/g, "\r"),
  );
  assert.equal(await count(), lineCount + 1);
  stage = "bracketed paste";
  f.writeShell(0, "\x1b[?2004h");
  await new Promise((r) => setTimeout(r, 70));
  await clip("bracket\nend\n");
  await focus();
  const bracketBefore = f.shellInputs[0],
    bracketCount = await count();
  await p.keyboard.press("Shift+Insert");
  await p.getByRole("button", { name: "确认粘贴", exact: true }).click();
  await waitFor(
    () =>
      f.shellInputs[0] === bracketBefore + "\x1b[200~bracket\rend\r\x1b[201~",
  );
  assert.equal(await count(), bracketCount + 1);
  f.writeShell(0, "\x1b[?2004l");
  stage = "asynchronous focus race";
  await clip("DO_NOT_SEND_AFTER_SWITCH");
  await app.evaluate(({ clipboard }) => {
    (globalThis as any).clipRead = clipboard.readText.bind(clipboard);
    clipboard.readText = (async () => {
      await new Promise((r) => setTimeout(r, 200));
      return (globalThis as any).clipRead();
    }) as typeof clipboard.readText;
  });
  await focus();
  const raceBefore = f.received,
    raceCount = await count();
  await p.keyboard.press("Control+v");
  await tab(b);
  await new Promise((r) => setTimeout(r, 350));
  assert.equal(f.received, raceBefore);
  assert.equal(await count(), raceCount);
  await app.evaluate(
    ({ clipboard }) => (clipboard.readText = (globalThis as any).clipRead),
  );
  await tab(a);
  stage = "empty oversized rejected";
  for (const text of ["", "a".repeat(1024 * 1024 + 1)]) {
    await clip(text);
    await focus();
    const previous = await count();
    await p.keyboard.press("Control+v");
    await terminal.getByRole("status").waitFor();
    await new Promise((r) => setTimeout(r, 50));
    assert.equal(await count(), previous);
  }
  stage = "plain input native editing";
  await clip("搜索中文😀");
  const search = p.getByLabel("搜索连接", { exact: true });
  await search.fill("old");
  await search.focus();
  await p.keyboard.press(`${mod}+a`);
  const inputCount = await count();
  await p.keyboard.press(`${mod}+v`);
  await until(
    p,
    () =>
      (document.querySelector('[aria-label="搜索连接"]') as HTMLInputElement)
        .value === "搜索中文😀",
    undefined,
  );
  await p.keyboard.press(`${mod}+z`);
  assert.equal(await search.inputValue(), "old");
  await search.fill("");
  assert.equal(await count(), inputCount);
  stage = "code editor native/context paste undo";
  await p.getByRole("button", { name: "打开文件浏览", exact: true }).click();
  await p
    .locator(
      ".workspace-files:not(.hidden) .file-source-view:not(.hidden) .file-row",
    )
    .filter({ hasText: "format.yml" })
    .getByRole("button", { name: "在线编辑", exact: true })
    .click();
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await editor.waitFor();
  const original = await readFile(path.join(f.directory, "format.yml"), "utf8"),
    body = original.slice(1).replace(/\r\n/g, "\n");
  const editorText = () =>
    editor.evaluate((el) => (el as any).cmTile.root.view.state.sliceDoc());
  await editor.click();
  await p.keyboard.press(`${mod}+a`);
  await clip("\ufeff粘贴😀\tvalue  \n末行  ");
  const editorEvents = await count();
  await p.keyboard.press(`${mod}+v`);
  await until(
    p,
    () =>
      (
        document.querySelector('[aria-label="远程文件内容"]') as any
      ).cmTile.root.view.state.doc
        .toString()
        .includes("末行"),
    undefined,
  );
  assert.equal(
    await readFile(path.join(f.directory, "format.yml"), "utf8"),
    original,
  );
  await p.keyboard.press(`${mod}+z`);
  assert.equal(await editorText(), body);
  await p.keyboard.press(`${mod}+Shift+z`);
  await p.keyboard.press(`${mod}+s`);
  await until(
    p,
    () =>
      !(
        document.querySelector(".editor-tabs .selected")?.textContent ?? ""
      ).includes("未保存"),
    undefined,
  );
  assert.equal(
    await readFile(path.join(f.directory, "format.yml"), "utf8"),
    "\ufeff粘贴😀\tvalue  \r\n末行  ",
  );
  assert.equal(await count(), editorEvents);
  await editor.click({ button: "right" });
  await p.getByRole("menuitem", { name: "全选", exact: true }).click();
  await editor.click({ button: "right" });
  await p.getByRole("menuitem", { name: "复制", exact: true }).click();
  assert.ok(
    await app.evaluate(async ({ clipboard }) =>
      (await clipboard.readText()).includes("粘贴😀"),
    ),
  );
  await editor.click({ button: "right" });
  await p.getByRole("menuitem", { name: "剪切", exact: true }).click();
  await until(
    p,
    () =>
      !(document.querySelector('[aria-label="远程文件内容"]') as any).cmTile
        .root.view.state.doc.length,
    undefined,
  );
  await editor.click({ button: "right" });
  await p.getByRole("menuitem", { name: "撤销", exact: true }).click();
  assert.ok((await editorText()).includes("末行"));
  stage = "real ANSI light rendering and selection";
  const normal = [
      "BLACK",
      "RED",
      "GREEN",
      "YELLOW",
      "BLUE",
      "MAGENTA",
      "CYAN",
      "WHITE",
    ],
    ansi =
      "\r\n" +
      normal.map((name, i) => `\x1b[${30 + i}m${name}\x1b[0m`).join(" ") +
      "\r\n" +
      normal
        .map((name, i) => `\x1b[${90 + i}mBRIGHT_${name}\x1b[0m`)
        .join(" ") +
      "\r\n";
  f.writeShell(0, ansi);
  await until(
    p,
    async (id) =>
      (await (window as any).forterm.attach(id)).includes("BRIGHT_WHITE"),
    a,
  );
  const beforeTheme = await p.evaluate(
      async (id) => (window as any).forterm.attach(id),
      a,
    ),
    auth = f.authentications;
  await p.getByTitle("切换主题", { exact: true }).click();
  await until(p, () => !!document.querySelector(".app.light"), undefined);
  assert.equal(
    await p.evaluate(async (id) => (window as any).forterm.attach(id), a),
    beforeTheme,
  );
  assert.equal(f.authentications, auth);
  await terminal.locator(".terminal-canvas").click();
  await p.screenshot({ path: `test-results/light-ansi-code-${version}.png` });
  await terminal.locator(".terminal-canvas").click({ button: "right" });
  await p.getByRole("menuitem", { name: "全选", exact: true }).click();
  await p.screenshot({
    path: `test-results/light-terminal-selection-${version}.png`,
  });
  await p.getByTitle("切换主题", { exact: true }).click();
  assert.equal(
    await p.evaluate(async (id) => (window as any).forterm.attach(id), a),
    beforeTheme,
  );
  assert.equal(f.shellInputs[1], "");
  assert.deepEqual(errors, []);
  console.log(
    "PASS: real native clipboard across Ctrl/Cmd/Shift-Insert/menu/native paste; selected-copy vs Ctrl-C interrupt; exactly one IPC input; multiline cancel/confirm and bracketed paste; delayed focus-switch cancellation; empty/limit; input undo; CodeMirror copy/cut/paste/undo/BOM/CRLF/tails/Ctrl-S with no terminal input; real ANSI light/selection/dark switch with stable buffer/authentication",
  );
} catch (e) {
  await app
    .windows()[0]
    ?.screenshot({ path: `test-results/clipboard-failure-${version}.png` })
    .catch(() => {});
  console.log("Clipboard UI stage:", stage);
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
