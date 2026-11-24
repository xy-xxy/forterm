#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
mkdir -p evidence/windows
export CGO_ENABLED=1 GOOS=windows GOARCH=amd64
: "${CC:=x86_64-w64-mingw32-gcc}"
: "${CXX:=x86_64-w64-mingw32-g++}"
export CC CXX
go build -trimpath -ldflags='-s -w -H windowsgui -extldflags=-static' -o evidence/windows/forterm-native.exe ./cmd/forterm
CGO_ENABLED=0 go build -trimpath -ldflags='-s -w -H windowsgui' -o evidence/windows/xshell.exe ./cmd/entry
cp evidence/windows/xshell.exe evidence/windows/xftp.exe
cp evidence/windows/xshell.exe evidence/windows/forterm-diagnose.exe
