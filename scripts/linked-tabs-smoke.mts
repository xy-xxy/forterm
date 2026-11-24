// Historical <= 0.1.6 UI scenario; the npm alias now runs three-column-smoke.mts.
import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, rm, readFile, writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { tmpdir } from "node:os";
import assert from "node:assert/strict";
import { fixture } from "../tests/fixture";
import { ftpFixture } from "../tests/ftp-fixture";
import { until, replaceCode } from "./ui-helpers.mts";
const dir = await mkdtemp(path.join(tmpdir(), "forterm-linked-tabs-")),
  f = await fixture(),
  ftp = await ftpFixture();
const env = { ...process.env, FORTERM_TEST_DATA: dir };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
const version = JSON.parse(await readFile("package.json", "utf8")).version;
try {
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  let trusted = false;
  const connect = async (protocol: "ssh" | "sftp" | "ftp", title: string) => {
    const id = await p.evaluate((c) => (window as any).forterm.connect(c), {
      protocol,
      title,
      host: "127.0.0.1",
      port: protocol === "ftp" ? ftp.port : f.port,
      username: protocol === "ftp" ? "test" : "ticket",
      password:
        protocol === "ftp" ? "synthetic-ftp-password" : "one-time@password",
      temporary: true,
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
  const tab = (id: string) => p.locator(`.tab[data-session-id="${id}"]`);
  const select = async (id: string) => {
    await tab(id).locator("button").first().click();
    await until(
      p,
      (id) =>
        document
          .querySelector(".tab.selected")
          ?.getAttribute("data-session-id") === id,
      id,
    );
  };
  const a = await connect("ssh", "资产终端 A"),
    source = await connect("sftp", "关联 SFTP 文件"),
    other = await connect("ftp", "备用 FTP 文件");
  await select(a);
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(source);
  await until(
    p,
    (id) => !document.querySelector(`.tab[data-session-id="${id}"]`),
    source,
  );
  assert.equal(await tab(other).count(), 1);
  assert.equal(await tab(a).count(), 1);
  await p
    .locator(".file-row")
    .filter({ hasText: "hello.txt" })
    .getByRole("button", { name: "在线编辑", exact: true })
    .click();
  await replaceCode(p, "编辑仍绑定隐藏的文件连接\n");
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(other);
  await p.getByRole("alert").filter({ hasText: "未保存" }).waitFor();
  assert.equal(
    await p.getByLabel("关联文件连接", { exact: true }).inputValue(),
    source,
  );
  assert.equal(await tab(source).count(), 0);
  assert.equal(await tab(other).count(), 1);
  await p.getByRole("button", { name: "保存文件", exact: true }).click();
  await until(
    p,
    () =>
      !document
        .querySelector(".editor-tabs .selected")
        ?.textContent?.includes("未保存"),
  );
  assert.equal(
    await readFile(path.join(f.directory, "hello.txt"), "utf8"),
    "编辑仍绑定隐藏的文件连接\n",
  );
  if (await p.getByRole("button", { name: "关闭提示", exact: true }).count())
    await p.getByRole("button", { name: "关闭提示", exact: true }).click();
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(other);
  await tab(source).waitFor();
  await until(
    p,
    (id) => !document.querySelector(`.tab[data-session-id="${id}"]`),
    other,
  );
  await p.locator(".file-row").filter({ hasText: "ftp.txt" }).waitFor();
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(source);
  await tab(other).waitFor();
  await until(
    p,
    (id) => !document.querySelector(`.tab[data-session-id="${id}"]`),
    source,
  );
  // Keyboard cycles only through the visible workspace tabs.
  await tab(a).focus();
  await p.keyboard.press("Control+Shift+]");
  await until(
    p,
    (id) =>
      document
        .querySelector(".tab.selected")
        ?.getAttribute("data-session-id") === id,
    other,
  );
  await p.keyboard.press("Control+Shift+]");
  await until(
    p,
    (id) =>
      document
        .querySelector(".tab.selected")
        ?.getAttribute("data-session-id") === id,
    a,
  );
  await p.getByRole("button", { name: "同时显示", exact: true }).click();
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
    `test-results/linked-file-tabs-${version}.png`,
    Buffer.from(png, "base64"),
  );
  const b = await connect("ssh", "资产终端 B");
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(source);
  await until(
    p,
    (id) => !document.querySelector(`.tab[data-session-id="${id}"]`),
    source,
  );
  await tab(a).getByRole("button", { name: "关闭会话", exact: true }).click();
  await until(
    p,
    (id) => !document.querySelector(`.tab[data-session-id="${id}"]`),
    a,
  );
  assert.equal(await tab(source).count(), 0);
  await p.getByLabel("关联文件连接", { exact: true }).selectOption(b);
  await tab(source).waitFor();
  // The same association event from another window redirects an active file tab to its owner.
  await select(source);
  await p.evaluate(
    ({ b, source }) => (window as any).forterm.linkFiles(b, source),
    { b, source },
  );
  await until(
    p,
    (id) =>
      document
        .querySelector(".tab.selected")
        ?.getAttribute("data-session-id") === id,
    b,
  );
  assert.equal(await tab(source).count(), 0);
  await tab(b).click({ button: "right" });
  await p.getByRole("menuitem", { name: "关闭其他标签", exact: true }).click();
  await until(
    p,
    async ({ b, source }) => {
      const s = (await (window as any).forterm.bootstrap()).sessions;
      return (
        s.length === 2 &&
        s.some((x: any) => x.id === b) &&
        s.some((x: any) => x.id === source && x.status === "ready")
      );
    },
    { b, source },
  );
  assert.equal(await tab(source).count(), 0);
  await tab(b).getByRole("button", { name: "关闭会话", exact: true }).click();
  await tab(source).waitFor();
  await until(
    p,
    (id) =>
      document
        .querySelector(".tab.selected")
        ?.getAttribute("data-session-id") === id,
    source,
  );
  assert.equal(
    (
      await p.evaluate(
        async () => (await (window as any).forterm.bootstrap()).sessions,
      )
    ).length,
    1,
  );
  assert.equal(f.authentications, 3);
  assert.deepEqual(errors, []);
  console.log(
    "PASS: linked SFTP/FTP tabs hide without closing connections; switching/unlinking/last-owner close restores tabs; shared owner keeps source hidden; hidden-source edit/save and dirty guard work; keyboard and close-others skip hidden sources; external focus redirects to owner; no reauthentication",
  );
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
