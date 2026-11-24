import { mkdir, copyFile } from "node:fs/promises";
import { execFileSync } from "node:child_process";
await mkdir("build/launchers", { recursive: true });
execFileSync(
  "go",
  [
    "build",
    "-trimpath",
    "-ldflags=-s -w -H=windowsgui",
    "-o",
    "build/launchers/xshell.exe",
    "launcher/main.go",
  ],
  {
    stdio: "inherit",
    env: { ...process.env, GOOS: "windows", GOARCH: "amd64", CGO_ENABLED: "0" },
  },
);
await copyFile("build/launchers/xshell.exe", "build/launchers/xftp.exe");
await copyFile(
  "build/launchers/xshell.exe",
  "build/launchers/forterm-diagnose.exe",
);
