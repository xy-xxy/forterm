import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { fixture } from "../tests/fixture";
import { until } from "./ui-helpers.mts";
const data = await mkdtemp(path.join(tmpdir(), "forterm-bom-history-")),
  f = await fixture();
for (const [name, value] of [
  ["plain.txt", "plain text\nlast line"],
  ["bom.txt", "\ufeffplain text\nlast line"],
])
  await writeFile(path.join(f.directory, name), value);
const env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
const mod = process.platform === "darwin" ? "Meta" : "Control";
try {
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const id = await p.evaluate(
    (port) =>
      (window as any).forterm.connect({
        protocol: "sftp",
        host: "127.0.0.1",
        port,
        username: "ticket",
        password: "one-time@password",
        title: "BOM 历史回归",
      }),
    f.port,
  );
  await p.getByRole("button", { name: "信任并连接", exact: true }).click();
  const open = async (name: string) => {
    await p
      .locator(".file-row")
      .filter({ hasText: name })
      .getByRole("button", { name: "在线编辑", exact: true })
      .click();
    await until(
      p,
      (name) =>
        document
          .querySelector(".editor-tabs .selected")
          ?.textContent?.includes(name) ?? false,
      name,
    );
  };
  for (const [name, bom] of [
    ["plain.txt", false],
    ["bom.txt", true],
  ] as const) {
    await open(name);
    const editor = p.getByLabel("远程文件内容", { exact: true });
    await editor.click();
    await p.keyboard.press(`${mod}+a`);
    await editor.evaluate((element) => {
      const clipboard = new DataTransfer();
      clipboard.setData("text/plain", "\ufeffinserted");
      element.dispatchEvent(
        new ClipboardEvent("paste", {
          clipboardData: clipboard,
          bubbles: true,
          cancelable: true,
        }),
      );
    });
    await until(
      p,
      () =>
        (
          document.querySelector('[aria-label="远程文件内容"]') as any
        ).cmTile.root.view.state.doc.toString() === "inserted",
      undefined,
    );
    await p.keyboard.press(`${mod}+z`);
    await until(
      p,
      () =>
        !(
          document.querySelector(".editor-tabs .selected")?.textContent ?? ""
        ).includes("未保存"),
      undefined,
    );
    assert.equal(
      await p
        .getByRole("button", { name: "保存文件", exact: true })
        .isDisabled(),
      true,
    );
    assert.equal(
      await readFile(path.join(f.directory, name), "utf8"),
      (bom ? "\ufeff" : "") + "plain text\nlast line",
    );
    await p.getByLabel("编辑器停靠位置", { exact: true }).selectOption("left");
    await open("hello.txt");
    await p.getByRole("button", { name, exact: true }).click();
    await p.getByLabel("远程文件内容", { exact: true }).click();
    await p.keyboard.press(`${mod}+Shift+z`);
    await until(
      p,
      () =>
        (
          document.querySelector('[aria-label="远程文件内容"]') as any
        ).cmTile.root.view.state.doc.toString() === "inserted",
      undefined,
    );
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
      await readFile(path.join(f.directory, name), "utf8"),
      (bom ? "\ufeff" : "") + "inserted",
    );
  }
  console.log(
    "PASS: real BOM-bearing browser paste into no-BOM and BOM originals; undo restores bytes/dirty, redo after file/dock switching preserves original BOM policy; explicit save bytes match; no production or Windows claim",
  );
} catch (e) {
  await mkdir("test-results", { recursive: true });
  await app
    .windows()[0]
    ?.screenshot({ path: "test-results/bom-history-failure-0.1.5.png" })
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
  await f.close();
  await rm(data, { recursive: true, force: true });
}
