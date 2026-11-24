import { until } from "./ui-helpers.mts";
import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { fixture, waitFor } from "../tests/fixture";
const dir = await mkdtemp(path.join(tmpdir(), "forterm-upload-ui-")),
  f = await fixture({
    maxOpenFiles: 1,
    writeDelayMs: 20,
    denyRealpathWhileOpen: true,
  });
const env = { ...process.env, FORTERM_TEST_DATA: path.join(dir, "profile") };
delete env.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({
  executablePath: electronPath as unknown as string,
  args: ["."],
  env,
});
await mkdir("test-results", { recursive: true });
try {
  const p = await app.firstWindow();
  p.setDefaultTimeout(15000);
  const errors: string[] = [];
  p.on("pageerror", (e) => errors.push(e.message));
  await p.getByRole("heading", { name: /每一个连接/ }).waitFor();
  const id = await p.evaluate(
    (port) =>
      (window as any).forterm.connect({
        protocol: "ssh",
        host: "127.0.0.1",
        port,
        username: "ticket",
        password: "one-time@password",
        temporary: true,
        title: "上传队列回归",
      }),
    f.port,
  );
  await p.getByRole("button", { name: "信任并连接", exact: true }).click();
  await p.getByRole("button", { name: "打开文件浏览", exact: true }).click();
  await p.locator(".file-row").filter({ hasText: "hello.txt" }).waitFor();
  const locals = [];
  for (let i = 0; i < 24; i++) {
    const file = path.join(dir, `file-${i}.bin`);
    await writeFile(file, Buffer.alloc(64000, i + 1));
    locals.push(file);
  }
  await app.evaluate(
    ({ dialog }, files) => {
      dialog.showOpenDialog = (async () => ({
        canceled: false,
        filePaths: files,
      })) as typeof dialog.showOpenDialog;
    },
    locals.slice(0, 20),
  );
  await p.evaluate((id) => (window as any).forterm.upload(id, "/"), id);
  await app.evaluate(({ dialog }, files) => {
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: files,
    })) as typeof dialog.showOpenDialog;
  }, locals.slice(20));
  await p.evaluate((id) => (window as any).forterm.upload(id, "/"), id);
  await mkdir(path.join(f.directory, "sub"));
  await writeFile(
    path.join(f.directory, "sub/marker.txt"),
    "new directory marker",
  );
  await p.getByLabel("远程目录", { exact: true }).fill("/sub");
  await p.getByLabel("远程目录", { exact: true }).press("Enter");
  await until(
    p,
    async () => {
      const b = await (window as any).forterm.bootstrap();
      return (
        b.transfers.length === 24 &&
        b.transfers.every((t: any) => !["queued", "running"].includes(t.state))
      );
    },
    null,
    30000,
  );
  const states = await p.evaluate(async () =>
    (await (window as any).forterm.bootstrap()).transfers.map(
      (t: any) => t.state,
    ),
  );
  assert.ok(
    states.every((s) => s === "done"),
    JSON.stringify(
      await p.evaluate(async () =>
        (await (window as any).forterm.bootstrap()).transfers.map((t: any) => ({
          name: t.name,
          state: t.state,
          message: t.message,
        })),
      ),
    ),
  );
  await waitFor(() => f.openFiles === 0);
  assert.equal(f.maxOpenFiles, 1);
  assert.equal(f.deniedOpen, 0);
  assert.equal(f.deniedRealpath, 0);
  for (let i = 0; i < 24; i++) {
    const a = createHash("sha256")
        .update(await readFile(locals[i]))
        .digest("hex"),
      b = createHash("sha256")
        .update(await readFile(path.join(f.directory, `file-${i}.bin`)))
        .digest("hex");
    assert.equal(a, b);
  }
  await p.locator(".file-row").filter({ hasText: "marker.txt" }).waitFor();
  assert.equal(
    await p.getByLabel("远程目录", { exact: true }).inputValue(),
    "/sub",
  );
  await p.getByLabel("远程目录", { exact: true }).fill("/");
  await p.getByLabel("远程目录", { exact: true }).press("Enter");
  await p.locator(".file-row").filter({ hasText: "file-23.bin" }).waitFor();
  f.setBehavior({ denyRealpath: true });
  await p.getByTitle("刷新", { exact: true }).click();
  await p.getByRole("alert").filter({ hasText: "目录路径解析" }).waitFor();
  assert.ok(
    (await p.locator(".file-row").count()) > 20,
    "failed refresh retains prior usable directory",
  );
  assert.ok(
    !(await p.getByRole("alert").innerText()).includes("Error invoking"),
  );
  f.setBehavior({ denyRealpath: false });
  await p.getByRole("button", { name: "重试读取", exact: true }).click();
  await p.getByRole("alert").waitFor({ state: "hidden" });
  const stalled = path.join(dir, "stalled.bin");
  await writeFile(stalled, Buffer.alloc(100000, 3));
  f.setBehavior({ stallWriteNames: ["stalled.bin"] });
  await app.evaluate(({ dialog }, file) => {
    dialog.showOpenDialog = (async () => ({
      canceled: false,
      filePaths: [file],
    })) as typeof dialog.showOpenDialog;
  }, stalled);
  await p.evaluate((id) => (window as any).forterm.upload(id, "/"), id);
  await until(
    p,
    async () =>
      (await (window as any).forterm.bootstrap()).transfers.at(-1).state ===
      "running",
  );
  await p.evaluate(async () => {
    const b = await (window as any).forterm.bootstrap();
    await (window as any).forterm.cancelTransfer(b.transfers.at(-1).id);
  });
  await until(
    p,
    async () =>
      (await (window as any).forterm.bootstrap()).transfers.at(-1).state ===
      "cancelled",
  );
  await waitFor(() => f.openFiles === 0);
  f.setBehavior({ stallWriteNames: [] });
  await p.getByTitle("刷新", { exact: true }).click();
  await p.locator(".file-row").filter({ hasText: "hello.txt" }).waitFor();
  await p.evaluate(
    (id) =>
      (window as any).forterm.input(
        id,
        "terminal retained after upload cancellation",
      ),
    id,
  );
  await waitFor(() => f.received.includes("terminal retained"));
  assert.equal(f.authentications, 1);
  assert.deepEqual(errors, []);
  await p.screenshot({ path: "test-results/upload-queue-0.1.5.png" });
  console.log(
    "PASS: 20 uploads plus append, one file handle, every SHA256, no listing denial during transfer; merged idle refresh; failed listing preserves rows and retries; active stall cancellation recovers SFTP without SSH reauth; terminal remains interactive",
  );
} catch (e) {
  await app
    .windows()[0]
    ?.screenshot({ path: "test-results/upload-queue-failure-0.1.5.png" })
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
  await app.close();
  await f.close();
  await rm(dir, { recursive: true, force: true });
}
