import { readFile, writeFile, stat } from "node:fs/promises";
import path from "node:path";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import assert from "node:assert/strict";
import { extractFile, listPackage } from "@electron/asar";
const version = JSON.parse(await readFile("package.json", "utf8")).version;
const root = path.resolve("release");
const names = [
  `Forterm-${version}-x64.zip`,
  `Forterm-${version}-x64-setup.exe`,
  `Forterm-${version}-x64-portable.exe`,
];
const hashes = [];
for (const name of names) {
  const content = await readFile(path.join(root, name));
  assert.ok(
    content.length > 1024 * 1024,
    `${name} must be a complete artifact`,
  );
  hashes.push(`${createHash("sha256").update(content).digest("hex")}  ${name}`);
}
await writeFile(
  path.join(root, `SHA256SUMS-${version}.txt`),
  hashes.join("\n") + "\n",
);
const zip = JSON.parse(
  execFileSync(
    "python3",
    [
      "-c",
      `
import zipfile,json,sys,hashlib,pathlib
with zipfile.ZipFile(sys.argv[1]) as z:
    assert z.testzip() is None, 'ZIP CRC mismatch'
    names=z.namelist()
    for entry in ['forterm.exe','xshell.exe','xftp.exe','forterm-diagnose.exe','resources/app.asar']:
        assert entry in names, 'missing '+entry
        assert hashlib.sha256(z.read(entry)).digest() == hashlib.sha256((pathlib.Path(sys.argv[2])/entry).read_bytes()).digest(), 'ZIP/unpacked mismatch: '+entry
    print(json.dumps({'crc':'passed','entries':len(names),'fourEntrypoints':True,'unpackedContentMatch':True}))
`,
      path.join(root, names[0]),
      path.join(root, "win-unpacked"),
    ],
    { encoding: "utf8" },
  ),
);
for (const entry of [
  "forterm.exe",
  "xshell.exe",
  "xftp.exe",
  "forterm-diagnose.exe",
]) {
  const data = await readFile(path.join(root, "win-unpacked", entry));
  assert.equal(data.toString("ascii", 0, 2), "MZ");
  const pe = data.readUInt32LE(0x3c);
  assert.equal(data.readUInt32LE(pe), 0x00004550);
  assert.equal(data.readUInt16LE(pe + 4), 0x8664, entry + " must be x64 PE");
}
const asar = path.join(root, "win-unpacked/resources/app.asar");
const packagedVersion = JSON.parse(
  extractFile(asar, "package.json").toString(),
).version;
assert.equal(packagedVersion, version);
const entries = listPackage(asar);
let verified = 0;
for (const entry of entries.filter((entry) =>
  /^\/dist\/.+\.(?:cjs|js|css|html|map)$/.test(entry),
)) {
  const relative = entry.slice(1);
  assert.deepEqual(
    extractFile(asar, relative),
    await readFile(relative),
    `packaged source mismatch: ${relative}`,
  );
  verified++;
}
assert.ok(verified >= 5);
const nodePty = path.join(
  root,
  "win-unpacked/resources/app.asar.unpacked/node_modules/node-pty/prebuilds/win32-x64/conpty.node",
);
assert.ok((await stat(nodePty)).size > 0);
const report = {
  version,
  checkedAt: new Date().toISOString(),
  zip,
  windowsEntrypoints: "four x64 PE files",
  packagedVersion,
  distFilesEqualToAsar: verified,
  windowsPtyPrebuild: "present",
  checksums: hashes,
  runtimeScope:
    "Cross-built and statically verified on macOS; Windows runtime and production bastion acceptance pending",
};
await writeFile(
  path.join(root, `VERIFICATION-${version}.json`),
  JSON.stringify(report, null, 2) + "\n",
);
console.log(JSON.stringify(report, null, 2));
