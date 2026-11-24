import type { KeepAliveSettings } from "./types";

export const DEFAULT_KEEP_ALIVE: Readonly<KeepAliveSettings> = {
  enabled: true,
  intervalSeconds: 15,
  sftpProbe: true,
};
export const KEEP_ALIVE_MISSES = 3;
