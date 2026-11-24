#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
FORTERM_DOTNET="${FORTERM_DOTNET:-/Users/xianyu/.cache/forterm-dotnet/dotnet}"
FORTERM_BUILD_TAG="${FORTERM_BUILD_TAG:-alpha33}"
for project in Forterm.Desktop Forterm.Launcher Forterm.Diagnose Forterm.Portable; do
 "$FORTERM_DOTNET" restore "src/$project" --locked-mode
done
for target in osx-arm64 win-x64; do
 "$FORTERM_DOTNET" publish src/Forterm.Desktop -c Release -r "$target" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "artifacts/$FORTERM_BUILD_TAG-$target"
 "$FORTERM_DOTNET" publish src/Forterm.Launcher -c Release -r "$target" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "artifacts/$FORTERM_BUILD_TAG-launcher-$target"
 "$FORTERM_DOTNET" publish src/Forterm.Diagnose -c Release -r "$target" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "artifacts/$FORTERM_BUILD_TAG-diagnose-$target"
done
export FORTERM_BUILD_TAG
python3 tooling/merge-entries.py osx-arm64 win-x64
python3 tooling/audit-native.py "$FORTERM_BUILD_TAG"
