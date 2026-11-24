// One bounded, read-only probe at a time, using an already open SFTP channel.
// A stalled probe stops the scheduler instead of accumulating queued requests.
export class SftpHeartbeat {
  private timer?: NodeJS.Timeout;
  private deadline?: NodeJS.Timeout;
  private pending = false;
  private stopped = false;
  constructor(
    intervalMs: number,
    private probe: () => Promise<void>,
    private timeoutMs = Math.max(intervalMs * 3, 5000),
  ) {
    this.timer = setInterval(() => this.tick(), intervalMs);
    this.timer.unref();
  }
  private tick() {
    if (this.stopped || this.pending) return;
    this.pending = true;
    this.deadline = setTimeout(() => this.stop(), this.timeoutMs);
    this.deadline.unref();
    void Promise.resolve()
      .then(() => {
        if (!this.stopped) return this.probe();
      })
      // Even an SFTP error status is a valid channel response. Do not retry
      // immediately, close the SSH terminal, or create another channel.
      .catch(() => {})
      .finally(() => {
        clearTimeout(this.deadline);
        this.deadline = undefined;
        this.pending = false;
      });
  }
  stop() {
    this.stopped = true;
    clearInterval(this.timer);
    clearTimeout(this.deadline);
    this.timer = undefined;
    this.deadline = undefined;
  }
}
