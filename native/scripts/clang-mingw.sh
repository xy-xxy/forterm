#!/bin/sh
# Clang frontend with the installed MinGW-w64 headers, libraries and PE linker.
set -eu
root=$(x86_64-w64-mingw32-gcc -print-sysroot)
libgcc=$(x86_64-w64-mingw32-gcc -print-file-name=libgcc.a)
linker=$(command -v x86_64-w64-mingw32-ld)
exec clang -Wno-unused-command-line-argument --target=x86_64-w64-windows-gnu --sysroot="$root" -L"${libgcc%/*}" -fuse-ld="$linker" "$@"
