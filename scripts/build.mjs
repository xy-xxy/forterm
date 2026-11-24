import { build } from "esbuild";
import { build as viteBuild } from "vite";
import { chmod } from "node:fs/promises";
if (process.platform === "darwin")
  await chmod(
    `node_modules/node-pty/prebuilds/darwin-${process.arch}/spawn-helper`,
    0o755,
  );
await build({
  entryPoints: ["src/main/index.ts"],
  bundle: true,
  platform: "node",
  format: "cjs",
  outfile: "dist/main/index.cjs",
  external: ["electron", "ssh2", "node-pty"],
  sourcemap: true,
});
await build({
  entryPoints: ["src/preload/index.ts"],
  bundle: true,
  platform: "node",
  format: "cjs",
  outfile: "dist/preload/index.cjs",
  external: ["electron"],
});
await viteBuild();
