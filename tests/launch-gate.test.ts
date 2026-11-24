import { test } from "node:test";
import assert from "node:assert/strict";
import { LaunchGate, LAUNCH_BURST_MS } from "../src/main/launch-gate";
import { parseLaunch } from "../src/main/launch";
import { mergeReports } from "../src/shared/reports";

const url = "ssh://opaque-ticket:SECRET%40credential@127.0.0.1:12024";
test("11 identical controller launches in 250ms claim exactly one connection", () => {
  let now = 0;
  const gate = new LaunchGate(() => now);
  let claims = 0;
  let record;
  for (let i = 0; i < 11; i++) {
    now = i * 25;
    const result = gate.accept(
      parseLaunch(["-newtab", `label-${i}`, "-url", url]),
    );
    if (!result.duplicate) claims++;
    record = result.record;
  }
  assert.equal(claims, 1);
  assert.equal(record?.report.invocations, 11);
  const serialized = JSON.stringify(record);
  for (const secret of ["opaque-ticket", "SECRET", "127.0.0.1", "label-"])
    assert.ok(!serialized.includes(secret));
});
test("diagnostic repetitions aggregate but distinct opaque tickets never merge", () => {
  const gate = new LaunchGate(() => 0);
  const one = gate.accept(parseLaunch(["--diagnose", "-url", url]));
  const repeat = gate.accept(parseLaunch(["--entry=diagnose", url]));
  const other = gate.accept(
    parseLaunch([
      "--diagnose",
      "-url",
      url.replace("opaque-ticket", "other-ticket"),
    ]),
  );
  assert.equal(repeat.duplicate, true);
  assert.equal(repeat.record, one.record);
  assert.equal(other.duplicate, false);
  assert.notEqual(other.record.report.id, one.record.report.id);
  assert.match(one.record.report.summary, /诊断模式未建立连接/);
});
test("each authentication, mode and transport boundary creates a separate request", () => {
  const gate = new LaunchGate(() => 0);
  gate.accept(parseLaunch([url]));
  for (const args of [
    ["--diagnose", url],
    ["--entry=xftp", url],
    ["-newwin", url],
    [url.replace("SECRET%40credential", "different")],
    [url.replace("12024", "12025")],
    ["-i", "private-key", url],
  ])
    assert.equal(gate.accept(parseLaunch(args)).duplicate, false);
});
test("dedupe expires after a bounded burst and explicit tab closure allows a new request", () => {
  let now = 0;
  const gate = new LaunchGate(() => now);
  const first = gate.accept(parseLaunch([url]));
  first.record.sessionId = "session1";
  now = 25;
  assert.equal(gate.accept(parseLaunch([url])).duplicate, true);
  gate.forgetSession("session1");
  assert.equal(gate.accept(parseLaunch([url])).duplicate, false);
  now = LAUNCH_BURST_MS + 25;
  assert.equal(gate.accept(parseLaunch([url])).duplicate, false);
});
test("unknown argument values are not grouped just because redacted reports look identical", () => {
  const gate = new LaunchGate(() => 0);
  assert.equal(
    gate.accept(parseLaunch(["--unknown", "secret1"])).duplicate,
    false,
  );
  assert.equal(
    gate.accept(parseLaunch(["--unknown", "secret2"])).duplicate,
    false,
  );
});
test("live report updates and delayed bootstrap do not duplicate cards or roll back counts", () => {
  const gate = new LaunchGate(() => 0);
  const first = {
    ...gate.accept(parseLaunch(["--diagnose", url])).record.report,
  };
  const updated = gate.accept(parseLaunch(["--diagnose", url])).record.report;
  const afterEvent = mergeReports([first], [updated]);
  const afterSnapshot = mergeReports([first], afterEvent);
  assert.equal(afterSnapshot.length, 1);
  assert.equal(afterSnapshot[0].invocations, 2);
});
