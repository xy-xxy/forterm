import { _electron as electron } from "playwright";
import electronPath from "electron";
import { mkdtemp, mkdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { setTimeout as delay } from "node:timers/promises";
import assert from "node:assert/strict";

const data = await mkdtemp(path.join(tmpdir(), "forterm-burst-"));
const env = { ...process.env, FORTERM_TEST_DATA: data };
delete env.ELECTRON_RUN_AS_NODE;
const packaged = process.env.FORTERM_PACKAGED_EXE;
const executable = packaged || electronPath;
const baseArgs = packaged ? [] : ["."];
const native = !!packaged && process.platform === "win32";
let sockets = 0;
const server = createServer((socket) => {
  sockets++;
  socket.on("error", () => {});
  socket.destroy();
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
const port = server.address().port;
const url = `ssh://BURST_TICKET:BURST_SECRET@127.0.0.1:${port}`;
const app = await electron.launch({
  executablePath: executable,
  args: baseArgs,
  env,
});
function invoke(entry, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      native
        ? path.join(
            path.dirname(packaged),
            entry === "diagnose" ? "forterm-diagnose.exe" : `${entry}.exe`,
          )
        : executable,
      [...baseArgs, ...(native ? [] : [`--entry=${entry}`]), ...args],
      { env, stdio: "ignore" },
    );
    const deadline = setTimeout(() => {
      child.kill();
      reject(
        new Error("Entry process did not hand off and exit within 15 seconds"),
      );
    }, 15000);
    child.once("error", (e) => {
      clearTimeout(deadline);
      reject(e);
    });
    child.once("exit", (code) => {
      clearTimeout(deadline);
      code === 0 ? resolve() : reject(new Error(`entry exited ${code}`));
    });
  });
}
async function stateUntil(page, predicate) {
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    const b = await page.evaluate(() => window.forterm.bootstrap());
    if (predicate(b)) return b;
    await delay(50);
  }
  throw new Error("Burst state timed out");
}
try {
  const page = await app.firstWindow();
  page.setDefaultTimeout(15000);
  await page.getByRole("heading", { name: /每一个连接/ }).waitFor();
  assert.equal(
    (await page.evaluate(() => window.forterm.bootstrap())).sessions.length,
    0,
  );
  await Promise.all(
    Array.from({ length: 11 }, (_, i) =>
      invoke("diagnose", ["-newtab", `private-label-${i}`, "-url", url]),
    ),
  );
  const diagnostic = await stateUntil(
    page,
    (b) => b.reports.reduce((n, r) => n + (r.invocations ?? 1), 0) >= 11,
  );
  assert.equal(diagnostic.reports.length, 1);
  assert.equal(diagnostic.reports[0].invocations, 11);
  assert.equal(diagnostic.sessions.length, 0);
  assert.equal(sockets, 0);
  await page.getByText("收到 11 次调用", { exact: false }).waitFor();
  const reportText = JSON.stringify(diagnostic.reports);
  assert.ok(!reportText.includes("BURST_"));
  assert.ok(!reportText.includes("private-label"));
  await mkdir("test-results", { recursive: true });
  await page.screenshot({ path: "test-results/burst-diagnosis.png" });

  await Promise.all(
    Array.from({ length: 11 }, (_, i) =>
      invoke("xshell", ["-newtab", `private-label-${i}`, "-url", url]),
    ),
  );
  const connected = await stateUntil(page, (b) =>
    b.sessions.some((s) => s.status === "error" && s.launchInvocations === 11),
  );
  assert.equal(connected.sessions.length, 1);
  assert.equal(sockets, 1);
  assert.equal(app.windows().length, 1);

  await invoke("xshell", ["-url", url.replace("BURST_TICKET", "OTHER_TICKET")]);
  const distinct = await stateUntil(
    page,
    (b) =>
      b.sessions.length === 2 && b.sessions.every((s) => s.status === "error"),
  );
  assert.equal(distinct.sessions.length, 2);
  assert.equal(sockets, 2);
  // Diagnostic and connection modes have different deduplication identities.
  assert.equal(distinct.reports.length, 1);
  console.log(
    "PASS: 11 diagnostic processes -> one report/count 11/no sockets; 11 SSH processes -> one session/one socket; different ticket -> separate session",
  );
} finally {
  await app.close();
  await new Promise((resolve) => server.close(resolve));
  await rm(data, { recursive: true, force: true });
}
