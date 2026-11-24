import { spawn } from "node:child_process";
import { build } from "esbuild";
import { createServer } from "vite";
import electron from "electron";
await build({
  entryPoints: ["src/main/index.ts"],
  bundle: true,
  platform: "node",
  format: "cjs",
  outfile: "dist/main/index.cjs",
  external: ["electron", "ssh2", "node-pty"],
});
await build({
  entryPoints: ["src/preload/index.ts"],
  bundle: true,
  platform: "node",
  format: "cjs",
  outfile: "dist/preload/index.cjs",
  external: ["electron"],
});
const server = await createServer();
await server.listen();
const env = { ...process.env, FORTERM_DEV_URL: "http://127.0.0.1:5173" };
delete env.ELECTRON_RUN_AS_NODE;
const child = spawn(electron, [".", ...process.argv.slice(2)], {
  stdio: "inherit",
  env,
});
child.on("exit", async (code) => {
  await server.close();
  process.exit(code ?? 0);
});
