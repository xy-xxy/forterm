import { _electron as electron, type Page, type Locator } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture, waitFor } from "../tests/fixture";
import { ftpFixture } from "../tests/ftp-fixture";
import { until } from "./ui-helpers.mts";
const version = JSON.parse(await readFile("package.json", "utf8")).version;
const dir = await mkdtemp(path.join(tmpdir(), "forterm-workbench-ui-")),
  f = await fixture(),
  ftp = await ftpFixture({ chmod: true });
const env = { ...process.env, FORTERM_TEST_DATA: path.join(dir, "profile") };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
await mkdir("test-results", { recursive: true });
const raw = '\ufeffroot:\r\n\tkey: "中文😀"  \r\n  flag: true';
await writeFile(path.join(f.directory, "config.yml"), raw);
await writeFile(
  path.join(f.directory, "second.json"),
  '{"value": 12, "flag": true}',
);
await writeFile(
  path.join(f.directory, "mixed.txt"),
  "first\r\nsecond\nthird  ",
);
await writeFile(
  path.join(f.directory, "long.properties"),
  Array.from({ length: 600 }, (_, i) => `key${i}=value${i}  `).join("\n"),
);
await mkdir(path.join(f.directory, "tree/sub"), { recursive: true });
await writeFile(path.join(f.directory, "tree/sub/a.txt"), "nested");
const mod = process.platform === "darwin" ? "Meta" : "Control";
async function cm(p: Page) {
  return p
    .getByLabel("远程文件内容", { exact: true })
    .evaluate((el) => (el as any).cmTile.root.view.state.sliceDoc());
}
async function replace(p: Page, text: string) {
  const editor = p.getByLabel("远程文件内容", { exact: true });
  await editor.click();
  await p.keyboard.press(`${mod}+a`);
  await p.keyboard.insertText(text);
  await until(
    p,
    (expected) =>
      (
        document.querySelector('[aria-label="远程文件内容"]') as any
      )?.cmTile.root.view.state.sliceDoc() === expected,
    text.replace(/\r\n|\r/g, "\n"),
  );
}
async function reachable(control: Locator) {
  await control.scrollIntoViewIfNeeded();
  const issues = await control.evaluate((el) => {
    const r = el.getBoundingClientRect(),
      out: string[] = [];
    if (
      r.top < -1 ||
      r.left < -1 ||
      r.bottom > innerHeight + 1 ||
      r.right > innerWidth + 1
    )
      out.push("viewport");
    for (let p = el.parentElement; p; p = p.parentElement) {
      const s = getComputedStyle(p),
        b = p.getBoundingClientRect();
      if (
        /auto|scroll|hidden|clip/.test(s.overflowY) &&
        (r.top < b.top - 1 || r.bottom > b.bottom + 1)
      )
        out.push(p.className);
      if (
        /auto|scroll|hidden|clip/.test(s.overflowX) &&
        (r.left < b.left - 1 || r.right > b.right + 1)
      )
        out.push(p.className);
    }
    const hit = document.elementFromPoint(
      r.x + r.width / 2,
      r.y + r.height / 2,
    );
    if (!hit || !el.contains(hit)) out.push("hit");
    return out;
  });
  assert.deepEqual(issues, []);
}
async function screenshot(p: Page, name: string) {
  await p.evaluate(
    () =>
      new Promise<void>((r) =>
        requestAnimationFrame(() => requestAnimationFrame(() => r())),
      ),
  );
  const bytes = await app.evaluate(async ({ BrowserWindow }) =>
    (await BrowserWindow.getAllWindows()[0].capturePage())
      .toPNG()
      .toString("base64"),
  );
  await writeFile(
    `test-results/${name}-${version}.png`,
    Buffer.from(bytes, "base64"),
  );
}
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
  const connect = async (
    protocol: "ssh" | "sftp" | "ftp",
    title: string,
    port: number,
  ) => {
    const id = await p.evaluate((c) => (window as any).forterm.connect(c), {
      protocol,
      host: "127.0.0.1",
      port,
      username: protocol === "ftp" ? "test" : "ticket",
      password:
        protocol === "ftp" ? "synthetic-ftp-password" : "one-time@password",
      temporary: true,
      title,
    });
    const host = p.getByRole("button", { name: "信任并连接", exact: true });
    if (await host.count()) await host.click();
    else if (protocol !== "ftp") {
      await until(
        p,
        async () => {
          const b = await (window as any).forterm.bootstrap();
          return (
            b.prompts.length > 0 || b.sessions.at(-1)?.status !== "connecting"
          );
        },
        undefined,
      );
      if (await host.count()) await host.click();
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
  const source = await connect(
    "sftp",
    "资产 A 的独立 SFTP — 很长的主机名称用于头部省略与工具可达性",
    f.port,
  );
  await p.locator(".file-row").filter({ hasText: "config.yml" }).waitFor();
  const hostToolbar = p.getByRole("toolbar", {
    name: "会话工具栏",
    exact: true,
  });
  const hostRects = await hostToolbar
    .locator(
      '.toolbar-title,button[aria-label="此会话的资源监控"],button[aria-label="在此会话打开终端"]',
    )
    .evaluateAll((nodes) => nodes.map((el) => el.getBoundingClientRect().y));
  assert.ok(Math.max(...hostRects) - Math.min(...hostRects) < 12);
  const row = p.getByRole("toolbar", {
    name: "文件路径筛选与操作",
    exact: true,
  });
  const positions = await row
    .locator(
      '[aria-label="远程目录"],[aria-label="筛选文件"],[aria-label="文件排序"]',
    )
    .evaluateAll((nodes) => nodes.map((el) => el.getBoundingClientRect().top));
  assert.ok(Math.max(...positions) - Math.min(...positions) < 2);
  await screenshot(p, "compact-sftp");
  const openFile = async (name: string) => {
    const target = p.locator(".file-row").filter({ hasText: name });
    await target.getByRole("button", { name: "在线编辑", exact: true }).click();
    await until(
      p,
      (name) =>
        document
          .querySelector(".editor-tabs button.selected")
          ?.textContent?.includes(name) ?? false,
      name,
    );
    await p.getByLabel("远程文件内容", { exact: true }).waitFor();
  };
  await openFile("config.yml");
  assert.equal(
    await p.getByLabel("编辑器停靠位置", { exact: true }).inputValue(),
    "right",
  );
  assert.equal(await cm(p), raw.slice(1).replace(/\r\n/g, "\n"));
  assert.equal(
    await p.getByLabel("显示空白与换行符", { exact: true }).isChecked(),
    false,
  );
  assert.equal(
    await p
      .locator(".cm-eol-marker,.cm-highlightSpace,.cm-highlightTab")
      .count(),
    0,
  );
  assert.ok((await p.locator(".cm-gutterElement").count()) >= 3);
  assert.ok((await p.locator(".cm-line span").count()) > 0);
  assert.ok(
    !(await p.locator(".editor-tabs .selected").innerText()).includes("未保存"),
  );
  await p.getByLabel("显示空白与换行符", { exact: true }).check();
  await until(
    p,
    () => document.querySelectorAll(".cm-eol-marker").length > 0,
    undefined,
  );
  assert.equal(
    await readFile(path.join(f.directory, "config.yml"), "utf8"),
    raw,
  );
  assert.ok(
    !(await p.locator(".editor-tabs .selected").innerText()).includes("未保存"),
  );
  await p.getByLabel("显示空白与换行符", { exact: true }).uncheck();
  assert.equal(
    await p
      .locator(".cm-eol-marker,.cm-highlightSpace,.cm-highlightTab")
      .count(),
    0,
  );
  for (const dock of ["left", "bottom", "right"]) {
    await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption(dock);
    assert.equal(await cm(p), raw.slice(1).replace(/\r\n/g, "\n"));
    assert.equal(
      await readFile(path.join(f.directory, "config.yml"), "utf8"),
      raw,
    );
  }
  await screenshot(p, "code-editor-right");
  await p.getByLabel("远程文件内容", { exact: true }).click();
  await p.keyboard.press(`${mod}+End`);
  await p.keyboard.insertText("!");
  assert.ok((await cm(p)).endsWith("true!"));
  await p.keyboard.press(`${mod}+z`);
  assert.equal(await cm(p), raw.slice(1).replace(/\r\n/g, "\n"));
  await p.keyboard.press(`${mod}+Shift+z`);
  assert.ok((await cm(p)).endsWith("true!"));
  await p.keyboard.press(`${mod}+s`);
  await until(
    p,
    () =>
      !(
        document.querySelector(".editor-tabs button.selected")?.textContent ??
        ""
      ).includes("未保存"),
    undefined,
  );
  assert.equal(
    await readFile(path.join(f.directory, "config.yml"), "utf8"),
    raw + "!",
  );
  assert.equal(f.received, "");
  await openFile("second.json");
  await replace(p, '{"changed": true}');
  await openFile("config.yml");
  assert.ok((await cm(p)).endsWith("true!"));
  await p
    .locator(
      `.editor-tabs [data-file-source-id="${source}"][data-document-path="/second.json"]`,
    )
    .click();
  assert.equal(await cm(p), '{"changed": true}');
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !(
        document.querySelector(".editor-tabs button.selected")?.textContent ??
        ""
      ).includes("未保存"),
    undefined,
  );
  await openFile("mixed.txt");
  assert.equal(
    await p
      .getByLabel("远程文件内容", { exact: true })
      .getAttribute("contenteditable"),
    "false",
  );
  for (const dock of ["left", "right", "bottom"]) {
    await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption(dock);
    assert.equal(
      await readFile(path.join(f.directory, "mixed.txt"), "utf8"),
      "first\r\nsecond\nthird  ",
    );
  }
  p.once("dialog", (d) => {
    void d.accept().catch(() => {});
  });
  await p.getByRole("button", { name: /Unix 换行后编辑/ }).click();
  assert.equal(
    await p
      .getByLabel("远程文件内容", { exact: true })
      .getAttribute("contenteditable"),
    "true",
  );
  assert.equal(
    await readFile(path.join(f.directory, "mixed.txt"), "utf8"),
    "first\r\nsecond\nthird  ",
  );
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !(
        document.querySelector(".editor-tabs button.selected")?.textContent ??
        ""
      ).includes("未保存"),
    undefined,
  );
  assert.equal(
    await readFile(path.join(f.directory, "mixed.txt"), "utf8"),
    "first\nsecond\nthird  ",
  );
  await p.getByTitle("新建文件", { exact: true }).click();
  await p.getByLabel("远程文件名称", { exact: true }).fill("new.yml");
  await p.getByRole("button", { name: "确认名称", exact: true }).click();
  await replace(p, "key: value  ");
  assert.equal(
    await p.evaluate(async () => {
      try {
        await (window as any).forterm.readText(
          (await (window as any).forterm.bootstrap()).sessions[0].id,
          "/new.yml",
        );
        return true;
      } catch {
        return false;
      }
    }),
    false,
  );
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !(
        document.querySelector(".editor-tabs button.selected")?.textContent ??
        ""
      ).includes("未保存"),
    undefined,
  );
  assert.equal(
    await readFile(path.join(f.directory, "new.yml"), "utf8"),
    "key: value  ",
  );
  // A first save publishes a source-bound refresh; no manual refresh is needed.
  await p.locator(".file-row").filter({ hasText: "new.yml" }).waitFor();
  // Duplicate names do not create or replace, and cancelling a blank-area draft is inert.
  await p.getByTitle("新建文件", { exact: true }).click();
  await p.getByLabel("远程文件名称", { exact: true }).fill("new.yml");
  await p.getByRole("button", { name: "确认名称", exact: true }).click();
  await p.getByRole("alert").filter({ hasText: "同名文件已存在" }).waitFor();
  assert.equal(
    await readFile(path.join(f.directory, "new.yml"), "utf8"),
    "key: value  ",
  );
  await p
    .locator(".inline-editor")
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await p.getByLabel("远程文件列表", { exact: true }).focus();
  await p.keyboard.press("Shift+F10");
  await p.getByRole("menuitem", { name: "新建文件", exact: true }).click();
  await p
    .getByLabel("远程文件名称", { exact: true })
    .fill("cancelled-draft.txt");
  await p.getByRole("button", { name: "确认名称", exact: true }).click();
  await replace(p, "draft only");
  p.once("dialog", (d) => {
    void d.dismiss().catch(() => {});
  });
  await p.getByRole("button", { name: "关闭编辑器", exact: true }).click();
  assert.equal(await cm(p), "draft only");
  p.once("dialog", (d) => {
    void d.accept().catch(() => {});
  });
  await p.getByRole("button", { name: "关闭编辑器", exact: true }).click();
  await until(
    p,
    () =>
      ![...document.querySelectorAll(".editor-tabs button")].some((el) =>
        el.textContent?.includes("cancelled-draft.txt"),
      ),
  );
  await assert.rejects(() =>
    readFile(path.join(f.directory, "cancelled-draft.txt")),
  );
  // Right-click target and native confirmation paths.
  await app.evaluate(({ dialog }) => {
    (globalThis as any).confirmation = 1;
    (globalThis as any).lastDialog = null;
    dialog.showMessageBox = (async (_w: any, options: any) => {
      (globalThis as any).lastDialog = options;
      return {
        response: (globalThis as any).confirmation,
        checkboxChecked: false,
      };
    }) as typeof dialog.showMessageBox;
  });
  const fileRow = p.locator(".file-row").filter({ hasText: "second.json" });
  await fileRow.click({ button: "right" });
  await p.getByRole("menuitem", { name: "复制路径", exact: true }).click();
  assert.ok(
    await app.evaluate(
      async ({ clipboard }) => (await clipboard.readText()) === "/second.json",
    ),
  );
  await fileRow.click({ button: "right" });
  await p.getByRole("menuitem", { name: "权限", exact: true }).click();
  await p.getByLabel("普通权限八进制", { exact: true }).fill("755");
  await p.getByRole("button", { name: "应用权限", exact: true }).click();
  await until(
    p,
    () => !document.querySelector('[aria-label="远程权限"]'),
    undefined,
  );
  const tree = p.locator(".file-row").filter({ hasText: "tree" });
  await tree.click({ button: "right" });
  await p
    .getByRole("menuitem", { name: "删除目录（含子项）", exact: true })
    .click();
  await until(
    p,
    async () =>
      (await (window as any).forterm.bootstrap()).fileOperations.some(
        (o: any) => o.path === "/tree" && o.state === "done",
      ),
    undefined,
  );
  const confirmation = await app.evaluate(() => (globalThis as any).lastDialog);
  assert.ok(
    confirmation.detail.includes("/tree") &&
      confirmation.message.includes("全部子"),
  );
  // Open a second asset at the same proxy and show there is no implicit association.
  const terminalA = await connect("ssh", "资产 A 终端", f.port),
    terminalB = await connect("ssh", "资产 B 终端（共用同一代理 IP）", f.port);
  const independent = await connect("ftp", "资产 B 独立 FTP", ftp.port);
  assert.deepEqual(
    await p.evaluate(
      async () => (await (window as any).forterm.bootstrap()).fileAssociations,
    ),
    {},
  );
  const tab = async (id: string) =>
    p
      .locator(`.tab[data-session-id="${id}"]`)
      .locator("button")
      .first()
      .click();
  await tab(terminalA);
  await p
    .locator(".workspace-files:not(.hidden)")
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(source);
  await until(
    p,
    async ({ a, source }) =>
      (await (window as any).forterm.bootstrap()).fileAssociations[a] ===
      source,
    { a: terminalA, source },
  );
  await tab(terminalB);
  assert.equal(
    await p
      .locator(".workspace-files:not(.hidden)")
      .getByLabel("关联文件连接", { exact: true })
      .inputValue(),
    terminalB,
  );
  await p
    .locator(".workspace-files:not(.hidden)")
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(independent);
  await p.locator(".file-row").filter({ hasText: "ftp.txt" }).waitFor();
  assert.equal(
    await p
      .getByRole("button", { name: "在线编辑", exact: true })
      .first()
      .isDisabled(),
    true,
  );
  assert.ok(
    (await p
      .locator(".workspace-files:not(.hidden)")
      .getByLabel("关联文件连接", { exact: true })
      .inputValue()) === independent,
  );
  await screenshot(p, "independent-ftp-workspace");
  await tab(terminalA);
  await p
    .locator(".file-row")
    .filter({ hasText: "config.yml" })
    .getByRole("button", { name: "在线编辑", exact: true })
    .click();
  await replace(p, "unsaved asset A");
  await p
    .locator(".workspace-files:not(.hidden)")
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(independent);
  assert.equal(await cm(p), "unsaved asset A");
  assert.equal(
    await p
      .locator(".workspace-files:not(.hidden)")
      .getByLabel("关联文件连接", { exact: true })
      .inputValue(),
    independent,
  );
  await p
    .locator(
      `.workspace-files:not(.hidden) .file-source-tag[data-file-source-id="${source}"] [role="tab"]`,
    )
    .click();
  await app.evaluate(() => ((globalThis as any).confirmation = 0));
  await p.getByRole("button", { name: "关闭编辑器", exact: true }).click();
  await until(p, () => !document.querySelector(".code-editor-host"), undefined);
  await app.evaluate(() => ((globalThis as any).confirmation = 1));
  // Native clipboard unified terminal paths and multiline confirmation.
  await app.evaluate(async ({ clipboard }) =>
    clipboard.writeText("粘贴😀\tONE"),
  );
  const activeTerm = p.locator(
    `[data-session-id="${terminalA}"] .xterm-helper-textarea`,
  );
  await activeTerm.focus();
  const before = f.received;
  await p.keyboard.press(
    process.platform === "darwin" ? "Meta+v" : "Control+Shift+v",
  );
  await waitFor(() => f.received.endsWith("粘贴😀\tONE"));
  assert.equal(f.received, before + "粘贴😀\tONE");
  await app.evaluate(async ({ clipboard }) =>
    clipboard.writeText("one\r\ntwo\n"),
  );
  await activeTerm.focus();
  await p.keyboard.press("Control+v");
  await p.getByRole("dialog", { name: "确认终端粘贴", exact: true }).waitFor();
  const prior = f.received;
  await p.getByRole("button", { name: "取消粘贴", exact: true }).click();
  assert.equal(f.received, prior);
  await activeTerm.focus();
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.paste(),
  );
  await p.getByRole("dialog", { name: "确认终端粘贴", exact: true }).waitFor();
  await p.getByRole("button", { name: "确认粘贴", exact: true }).click();
  await waitFor(() => f.received.endsWith("one\rtwo\r"));
  const noSelect = f.received;
  await activeTerm.focus();
  await p.keyboard.press("Control+c");
  await waitFor(() => f.received === noSelect + "\x03");
  // Theme changes do not recreate buffers, IDs or authentication.
  const stateBefore = await p.evaluate(
      async (id) => ({
        buffer: await (window as any).forterm.attach(id),
        sessions: (await (window as any).forterm.bootstrap()).sessions.map(
          (s: any) => s.id,
        ),
      }),
      terminalA,
    ),
    authBefore = f.authentications;
  await p.getByTitle("切换主题", { exact: true }).click();
  await screenshot(p, "light-terminal-editor");
  const stateAfter = await p.evaluate(
    async (id) => ({
      buffer: await (window as any).forterm.attach(id),
      sessions: (await (window as any).forterm.bootstrap()).sessions.map(
        (s: any) => s.id,
      ),
    }),
    terminalA,
  );
  assert.deepEqual(stateAfter, stateBefore);
  assert.equal(f.authentications, authBefore);
  assert.equal(await p.locator(".app.light").count(), 1);
  await p.getByTitle("切换主题", { exact: true }).click();
  // At high zoom, each changed toolbar stays one row and its controls can be reached.
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.setSize(1050, 650);
    w.webContents.setZoomFactor(1.5);
  });
  await p
    .locator(".workspace-files:not(.hidden)")
    .getByLabel("关联文件连接", { exact: true })
    .selectOption(source);
  await p.getByRole("button", { name: /^(收起连接栏|展开连接栏)$/ }).click();
  for (const control of [
    p
      .locator(".workspace-files:not(.hidden) .file-source-view:not(.hidden)")
      .getByLabel("远程目录", { exact: true }),
    p
      .locator(".workspace-files:not(.hidden) .file-source-view:not(.hidden)")
      .getByLabel("筛选文件", { exact: true }),
    p
      .locator(".workspace-files:not(.hidden) .file-source-view:not(.hidden)")
      .getByLabel("文件排序", { exact: true }),
    p.getByRole("button", { name: "在线编辑", exact: true }).first(),
  ])
    await reachable(control);
  await screenshot(p, "workbench-high-zoom");
  // Tab context targets the clicked asset; cancelling a dirty source stops close-others.
  await app.evaluate(({ BrowserWindow }) => {
    const w = BrowserWindow.getAllWindows()[0];
    w.webContents.setZoomFactor(1);
    w.setSize(1440, 940);
  });
  await tab(terminalA);
  await openFile("new.yml");
  await replace(p, "keep dirty source");
  await app.evaluate(() => ((globalThis as any).confirmation = 0));
  const keep = p.locator(".tab").filter({ hasText: "资产 B 终端" });
  await keep.click({ button: "right" });
  await p.getByRole("menuitem", { name: "关闭其他标签", exact: true }).click();
  await until(p, () => !document.querySelector('[role="menu"]'));
  assert.equal(
    await p.evaluate(
      async () => (await (window as any).forterm.bootstrap()).sessions.length,
    ),
    4,
  );
  await app.evaluate(() => ((globalThis as any).confirmation = 1));
  await keep.click({ button: "right" });
  await p.getByRole("menuitem", { name: "关闭其他标签", exact: true }).click();
  await until(
    p,
    async (id) => {
      const b = await (window as any).forterm.bootstrap();
      return (
        b.sessions.length === 3 &&
        b.sessions.some((s: any) => s.id === id) &&
        !b.sessions.some((s: any) => s.title === "资产 A 终端")
      );
    },
    terminalB,
  );
  assert.deepEqual(errors, []);
  console.log(
    "PASS: compact headers and path/filter/sort row; code gutter/highlight/whitespace opt-in; raw BOM/CRLF/tails, mixed read-only/explicit conversion; dock switching, multiple files/history, Ctrl-S; exclusive new file; file menu path/permissions/nonempty deletion; explicit FTP/SFTP linking without shared-IP auto-match and dirty switch protection; native clipboard single/multiline cancel/confirm/interrupt; live light terminal without reconnect; high zoom control reachability",
  );
} catch (e) {
  await app
    .windows()[0]
    ?.screenshot({ path: `test-results/workbench-failure-${version}.png` })
    .catch(() => {});
  console.log(
    await app
      .windows()[0]
      ?.locator("body")
      .innerText()
      .catch(() => "unavailable"),
  );
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
  await rm(dir, { recursive: true, force: true });
}
