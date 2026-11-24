import { test } from "node:test";
import assert from "node:assert/strict";
import { parseLaunch, parseURL } from "../src/main/launch";
const url = "ssh://session-abc:p%40ss%3Aword%25%2B@127.0.0.1:12024";
test("Xshell URL variants preserve opaque credentials and endpoint", () => {
  for (const args of [
    [url],
    ["-URL", url],
    ["-NewTab", "测试会话", "-url", url],
    ["-newwin", url],
    ["-newwin", "-url", url],
  ]) {
    const result = parseLaunch(args);
    assert.equal(result.error, undefined);
    assert.equal(result.connection?.username, "session-abc");
    assert.equal(result.connection?.password, "p@ss:word%+");
    assert.equal(result.connection?.port, 12024);
    assert.equal(result.connection?.temporary, true);
  }
});
test("Xftp opens file mode with either ssh or sftp URLs", () => {
  assert.equal(parseLaunch(["--entry=xftp", url]).connection?.protocol, "sftp");
  assert.equal(
    parseLaunch([url.replace("ssh:", "sftp:")]).connection?.protocol,
    "sftp",
  );
});
test("diagnostics never disclose credentials or unknown options", () => {
  const secrets = [
    "secret-token",
    "session-abc",
    "p@ss",
    "untrusted-label",
    "127.0.0.1",
  ];
  const result = parseLaunch([
    "--entry=diagnose",
    "-newtab",
    "untrusted-label",
    "-url",
    url,
    "--token=secret-token",
    "opaque-value",
  ]);
  assert.equal(result.diagnose, true);
  assert.equal(result.report.supported, false);
  const report = JSON.stringify(result.report);
  for (const s of secrets) assert.ok(!report.includes(s));
  assert.ok(!report.includes("opaque-value"));
});
test("bad URLs fail without echoing the input", () => {
  for (const v of [
    "ssh://a:supersecret@",
    "https://a:supersecret@example.org",
    "ssh://a:supersecret@host:99999",
    "ssh://a:%zz@host",
    "ssh://a:supersecret@host/path",
    "ssh://a:supersecret@host?token=x",
  ]) {
    assert.throws(() => parseURL(v));
    assert.ok(
      !JSON.stringify(parseLaunch(["-url", v]).report).includes("supersecret"),
    );
  }
});
test("IPv6, empty password and percent escapes are handled once", () => {
  const c = parseURL("ssh://a:p%2525@[::1]:2222");
  assert.equal(c.host, "::1");
  assert.equal(c.password, "p%25");
  assert.equal(parseURL("ssh://a@host").password, "");
});
test("missing, duplicate and unknown arguments do not initiate a partial connection", () => {
  for (const args of [
    ["-url"],
    ["-url", "-newtab"],
    [url, url],
    ["-unknown", "secret", url],
  ])
    assert.equal(parseLaunch(args).connection, undefined);
});
test("quoted path, label and URL values are supported", () => {
  const r = parseLaunch([
    "-newtab",
    '"名称 空格"',
    "-i",
    "C:\\keys\\my key",
    "-url",
    '"' + url + '"',
  ]);
  assert.equal(r.connection?.title, "名称 空格");
  assert.equal(r.connection?.keyPath, "C:\\keys\\my key");
});
