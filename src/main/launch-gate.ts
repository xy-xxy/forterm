import { createHmac, randomBytes, randomUUID } from "node:crypto";
import { performance } from "node:perf_hooks";
import type { Launch, Report } from "../shared/types";

export const LAUNCH_BURST_MS = 3000;
export interface LaunchRecord {
  report: Report;
  sessionId?: string;
  windowId?: number;
}

// Only external launches pass through this gate. Manual connections remain
// independent. No original URL, password or stable credential digest is retained.
export class LaunchGate {
  #key = randomBytes(32);
  #recent = new Map<string, { started: number; record: LaunchRecord }>();

  constructor(private clock: () => number = () => performance.now()) {}

  accept(launch: Launch): { duplicate: boolean; record: LaunchRecord } {
    const now = this.clock();
    for (const [key, entry] of this.#recent) {
      if (now - entry.started >= LAUNCH_BURST_MS) this.#recent.delete(key);
    }
    const c = launch.connection;
    // A redacted report cannot prove two unknown/invalid commands were equal.
    const key =
      c && !launch.error
        ? createHmac("sha256", this.#key)
            .update(
              JSON.stringify([
                launch.diagnose,
                launch.newWindow,
                c.protocol,
                c.host,
                c.port,
                c.username,
                c.password,
                c.keyPath,
                c.passphrase,
                c.jumpId,
              ]),
            )
            .digest("hex")
        : undefined;
    const existing = key ? this.#recent.get(key) : undefined;
    if (existing) {
      existing.record.report = {
        ...existing.record.report,
        invocations: (existing.record.report.invocations ?? 1) + 1,
        lastTime: launch.report.time,
      };
      return { duplicate: true, record: existing.record };
    }
    const record: LaunchRecord = {
      report: {
        ...launch.report,
        id: randomUUID(),
        invocations: 1,
        lastTime: launch.report.time,
      },
    };
    if (key) {
      // Bound the cache even under a flood of different launch requests.
      if (this.#recent.size >= 128)
        this.#recent.delete(this.#recent.keys().next().value!);
      this.#recent.set(key, { started: now, record });
    }
    return { duplicate: false, record };
  }

  forgetSession(id: string) {
    for (const [key, entry] of this.#recent) {
      if (entry.record.sessionId === id) this.#recent.delete(key);
    }
  }
}
