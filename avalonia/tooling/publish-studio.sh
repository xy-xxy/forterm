#!/usr/bin/env bash
# Publish Forterm Studio with its four entries next to each other:
#   Forterm(.exe)            main program
#   xshell(.exe), xftp(.exe) controller entries (renamed Forterm.Launcher apphost)
#   forterm-diagnose(.exe)   redacted argument report, no network
# Output: artifacts/studio-<version>-<rid>/ and, for Windows, ../release/Forterm-Studio-<version>-windows-x64.zip
# Never overwrites an existing output; bump <Version> in src/Forterm.Studio/Forterm.Studio.csproj instead.
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${FORTERM_DOTNET:-$HOME/.cache/forterm-dotnet/dotnet}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Forterm.Studio/Forterm.Studio.csproj | head -1)"
RIDS="${FORTERM_RIDS:-win-x64 osx-arm64}"
[ -n "$VERSION" ] || { echo "version not found" >&2; exit 1; }

for rid in $RIDS; do
  out="artifacts/studio-$VERSION-$rid"
  [ -e "$out" ] && { echo "Refusing to overwrite $out" >&2; exit 1; }
  for project in Forterm.Studio Forterm.Launcher Forterm.Diagnose; do
    "$DOTNET" restore "src/$project" --locked-mode
    "$DOTNET" publish "src/$project" -c Release -r "$rid" --self-contained true --no-restore \
      -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$out"
  done
  ext=""; [[ "$rid" == win-* ]] && ext=".exe"
  cp "$out/Forterm.Launcher$ext" "$out/xshell$ext"
  cp "$out/Forterm.Launcher$ext" "$out/xftp$ext"
  mv "$out/Forterm.Diagnose$ext" "$out/forterm-diagnose$ext"
  rm "$out/Forterm.Launcher$ext"
  find "$out" -name '*.pdb' -delete
  for entry in "Forterm$ext" "xshell$ext" "xftp$ext" "forterm-diagnose$ext"; do
    [ -f "$out/$entry" ] || { echo "missing entry $entry" >&2; exit 1; }
  done
  echo "$rid: four entries ready in $out"
done

if [[ " $RIDS " == *" win-x64 "* ]]; then
  zip_path="../release/Forterm-Studio-$VERSION-windows-x64.zip"
  [ -e "$zip_path" ] && { echo "Refusing to overwrite $zip_path" >&2; exit 1; }
  mkdir -p ../release
  stage="$(mktemp -d)"
  cp -R "artifacts/studio-$VERSION-win-x64" "$stage/Forterm-Studio-$VERSION"
  cp -R licenses "$stage/Forterm-Studio-$VERSION/licenses"
  (cd "$stage" && zip -qr -X "$OLDPWD/$zip_path" "Forterm-Studio-$VERSION")
  rm -rf "$stage"
  shasum -a 256 "$zip_path" | tee "$zip_path.sha256"
fi
