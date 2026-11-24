import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, writeFile, readFile, mkdir, rm } from "node:fs/promises";
import path from "node:path";
import { tmpdir } from "node:os";
import assert from "node:assert/strict";
import { fixture } from "../tests/fixture";
import { until, replaceCode } from "./ui-helpers.mts";
const dir = await mkdtemp(path.join(tmpdir(), "forterm-file-guards-"));
const f = await fixture({ writeDelayMs: 30 });
await mkdir(path.join(f.directory, "tree"));
const upload = path.join(dir, "upload.bin");
await writeFile(upload, Buffer.alloc(1024 * 1024, 65));
const env = { ...process.env, FORTERM_TEST_DATA: path.join(dir, "profile") };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
try {
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const id = await p.evaluate(
    (port) =>
      (window as any).forterm.connect({
        protocol: "sftp",
        host: "127.0.0.1",
        port,
        username: "ticket",
        password: "one-time@password",
        title: "文件保护专项",
      }),
    f.port,
  );
  await p.getByRole("button", { name: "信任并连接", exact: true }).click();
  await p
    .locator(".file-row")
    .filter({ hasText: "hello.txt" })
    .getByRole("button", { name: "在线编辑", exact: true })
    .click();
  await replaceCode(p, "keep this draft");
  await until(
    p,
    () =>
      document
        .querySelector(".editor-tabs .selected")
        ?.textContent?.includes("未保存") ?? false,
  );
  await app.evaluate(({ dialog }, file) => {
    (globalThis as any).guardDialogs = 0;
    dialog.showMessageBox = (async () => {
      (globalThis as any).guardDialogs++;
      return { response: 1, checkboxChecked: false };
    }) as typeof dialog.showMessageBox;
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: [file],
    })) as typeof dialog.showOpenDialog;
  }, upload);
  const mutation = async (action: "remove" | "permissions") =>
    p.evaluate(
      async ({ id, action }) => {
        try {
          if (action === "remove")
            await (window as any).forterm.remove(id, "/hello.txt", false);
          else
            await (window as any).forterm.setFilePermissions(
              id,
              "/hello.txt",
              493,
              420,
            );
          return "unexpected success";
        } catch (e) {
          return String(e);
        }
      },
      { id, action },
    );
  assert.match(await mutation("remove"), /未保存/);
  assert.match(await mutation("permissions"), /未保存/);
  assert.equal(await app.evaluate(() => (globalThis as any).guardDialogs), 0);
  assert.equal(
    await readFile(path.join(f.directory, "hello.txt"), "utf8"),
    "hello 文件\n",
  );
  // Electron's JS-confirm maps response 0 to the first (accept) button.
  await app.evaluate(({ dialog }) => {
    dialog.showMessageBox = (async () => ({
      response: 0,
      checkboxChecked: false,
    })) as typeof dialog.showMessageBox;
  });
  // Closing the real draft updates both renderer and main dirty registration.
  await p.getByRole("button", { name: "关闭编辑器", exact: true }).click();
  await until(p, () => !document.querySelector(".code-editor-host"));
  await p.evaluate(async (id) => {
    await (window as any).forterm.setDirty(id, false);
    await (window as any).forterm.upload(id, "/tree");
  }, id);
  await until(p, async () =>
    (await (window as any).forterm.bootstrap()).transfers.some(
      (t: any) => t.state === "running",
    ),
  );
  const during = await p.evaluate(async (id) => {
    try {
      await (window as any).forterm.remove(id, "/tree", true);
      return "unexpected success";
    } catch (e) {
      return String(e);
    }
  }, id);
  assert.match(during, /传输/);
  await until(p, async () =>
    (await (window as any).forterm.bootstrap()).transfers.every(
      (t: any) => t.state === "done",
    ),
  );
  assert.equal(
    (await readFile(path.join(f.directory, "tree/upload.bin"))).length,
    1024 * 1024,
  );
  // A second window could become dirty while native delete confirmation is open.
  await app.evaluate(({ dialog }) => {
    dialog.showMessageBox = (async () => {
      (globalThis as any).waitingGuard = true;
      return await new Promise((resolve) => {
        (globalThis as any).resolveGuard = resolve;
      });
    }) as typeof dialog.showMessageBox;
  });
  const pending = p.evaluate(async (id) => {
    try {
      await (window as any).forterm.remove(id, "/hello.txt", false);
      return "unexpected success";
    } catch (e) {
      return String(e);
    }
  }, id);
  for (
    let i = 0;
    i < 100 && !(await app.evaluate(() => (globalThis as any).waitingGuard));
    i++
  )
    await new Promise((r) => setTimeout(r, 20));
  assert.equal(
    await app.evaluate(() => (globalThis as any).waitingGuard),
    true,
  );
  await p.evaluate((id) => (window as any).forterm.setDirty(id, true), id);
  await app.evaluate(() =>
    (globalThis as any).resolveGuard({ response: 1, checkboxChecked: false }),
  );
  assert.match(await pending, /确认期间.*未保存/);
  assert.equal(
    await readFile(path.join(f.directory, "hello.txt"), "utf8"),
    "hello 文件\n",
  );
  assert.deepEqual(errors, []);
  await p.evaluate((id) => (window as any).forterm.setDirty(id, false), id);
  console.log(
    "PASS: real editor dirty registration blocks deletion/permissions without a native destructive dialog; actual running upload protects subtree; dirty change during native confirmation is rechecked before deletion; original bytes and transfer preserved",
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
  await rm(dir, { recursive: true, force: true });
}
