const fs = require("node:fs/promises");
const path = require("node:path");
module.exports = async (context) => {
  if (context.electronPlatformName === "darwin") {
    const helper = path.join(
      context.appOutDir,
      "Forterm.app/Contents/Resources/app.asar.unpacked/node_modules/node-pty/prebuilds/darwin-arm64/spawn-helper",
    );
    await fs.chmod(helper, 0o755);
  }
  if (context.electronPlatformName !== "win32") return;
  for (const entry of ["xshell.exe", "xftp.exe", "forterm-diagnose.exe"])
    await fs.copyFile(
      path.join(__dirname, "../build/launchers", entry),
      path.join(context.appOutDir, entry),
    );
  // Keep only N-API binaries for the packaged Windows architecture; a local
  // macOS build must never shadow Windows prebuilds through build/Release.
  const pty = path.join(
    context.appOutDir,
    "resources/app.asar.unpacked/node_modules/node-pty",
  );
  await fs.rm(path.join(pty, "build"), { recursive: true, force: true });
  const binary = path.join(pty, "prebuilds/win32-x64/pty.node");
  // node-pty names the Windows module conpty.node, depending on release.
  const files = await fs.readdir(path.dirname(binary));
  if (!files.some((x) => x.endsWith(".node")))
    throw new Error("Windows node-pty prebuild is missing");
};
