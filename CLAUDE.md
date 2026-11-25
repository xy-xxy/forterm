# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Forterm is a bastion-host (堡垒机) terminal plus file manager for Windows 10/11 x64. A single-sign-on controller launches it with Xshell/Xftp-compatible arguments (`xshell.exe -newtab <name> -url ssh://ticket:pw@host:port`) to open temporary SSH/SFTP sessions. The repo holds **three generations of the same product**. Each has its own source, packages, config directory and data realm. Earlier generations are deliberately kept frozen:

| Dir | Stack | Version | Status |
|---|---|---|---|
| repo root (`src/`, `tests/`, `scripts/`, `launcher/`) | Electron 45, TypeScript, React, xterm.js, ssh2, node-pty | 0.1.7 | frozen baseline |
| `native/` | Go 1.26 + Fyne | 0.2.x | frozen |
| `avalonia/` (`src/Forterm.Desktop`) | C# / .NET 10 + Avalonia 12 | 0.3.0-alpha.36 | frozen |
| `avalonia/` (`src/Forterm.Studio`) | same stack, AXAML + MVVM | 0.4.0-alpha.N | **active development** |

**Studio** is an XTerminal-style UI rebuild. It reuses `Forterm.Core` and targets feature parity with alpha.36. The plan, the user's decisions and the phase status live in `docs/STUDIO-REBUILD-PLAN.md`. Read it before Studio work. The user explicitly overrode some invariants listed below for Studio:
- terminal writes such as "CD to terminal", initial script, keepalive command and pwd follow are allowed;
- connection-list latency is a TCP-handshake timing only;
- metrics are shown only for sessions already open;
- the built-in editor is the only editor.

The reference screenshots in `资料/` contain real IPs. Never commit or publish them.

Unless the task names an older generation, work in `avalonia/src/Forterm.Studio`. Don't touch the Electron, Go or `Forterm.Desktop` sources, their packages, or their configs. Docs, UI strings, error messages and delivery notes are all in Chinese.

## Commands

### Avalonia (current): run from `avalonia/`

`dotnet` is not on PATH. Use the pinned SDK. `global.json` pins 10.0.403 with `rollForward: disable`:

```sh
DOTNET=/Users/xianyu/.cache/forterm-dotnet/dotnet
$DOTNET build src/Forterm.Desktop
$DOTNET test tests/Forterm.Tests                                   # full suite (~241 tests)
$DOTNET test tests/Forterm.Tests --filter "FullyQualifiedName~TotpTests"   # one class
$DOTNET test tests/Forterm.Tests --filter "FullyQualifiedName~TotpTests.SomeMethod"
FORTERM_BUILD_TAG=alpha36 tooling/publish.sh                       # self-contained osx-arm64 + win-x64 → artifacts/<tag>-<rid>
python3 tooling/package-checkpoint.py 0.3.0-alpha.36               # freeze into ../checkpoints and ../release (refuses to overwrite)
python3 tooling/package-windows.py ...                             # NSIS installer + single-file portable from the frozen ZIP
```

- Studio:
  - Build with `$DOTNET build src/Forterm.Studio` and test with `$DOTNET test tests/Forterm.Studio.Tests`.
  - `tooling/publish-studio.sh` writes the four entries to `artifacts/studio-<ver>-<rid>` and creates `../release/Forterm-Studio-<ver>-windows-x64.zip`. It refuses to overwrite either.
  - The Studio assembly is deliberately named `Forterm`, because the xshell/xftp launcher starts `Forterm(.exe)`.
  - Its data directory is `FORTERM_STUDIO_DATA`, defaulting to `LocalAppData/Forterm/Studio`. That directory also names the single-instance pipe.
  - To check the packaged entry chain without a window, run `xshell --diagnose -url …` and confirm the exit code is forwarded.
- `Forterm.Core` is shared with the frozen `Forterm.Desktop`. Changes to Core must be additive and must keep the old defaults. After a Core change, build `src/Forterm.Desktop` and run the related `tests/Forterm.Tests` classes, for example `--filter "FullyQualifiedName~TransportTests|FullyQualifiedName~JumpTests"`.
- Studio tests:
  - They use `StudioHarness` (`tests/Forterm.Studio.Tests/TestSupport.cs`): a temporary data directory, `MemoryVault` instead of the OS credential store, and `ScriptedInteraction` for dialogs and pickers.
  - The library rebuilds on the UI dispatcher, so call `Dispatcher.UIThread.RunJobs()` after changing the store and before asserting.
  - A terminal that has no template yet must attach through `StudioTerminalControl.AttachWhenReady`, never through `AttachConnection` directly.
  - Every member added to `IStudioInteraction` also needs a scripted version in `ScriptedInteraction`.
  - A module initializer in `TestSupport.cs` restricts the library's TCP latency probe to loopback, so library tests that use documentation addresses (198.51.100.x, 203.0.113.x) open no sockets.
  - Only `[AvaloniaFact]` tests may create view models or touch `StudioHarness.Model`. Plain `[Fact]` tests that touch the dispatcher first bind it to the wrong thread, and the suite then hangs intermittently.
  - `[AvaloniaFact]` tests must not use `TestContext.Current`: on the UI thread it can be the disposed context of an earlier test (seen on Windows). Two quick headless clicks on one row are a double click, which opens the file; on Windows an open file cannot be deleted.
  - CI: `.github/workflows/studio-windows.yml` runs the Studio tests and the packaged entry chain on `windows-latest`.
- Studio remote deletes go through Core's `RemoteFileOperations.RemoveNodeByMetadataAsync`, never through the content-hash `RemoveAsync`. That path:
  - checks lstat type, link status and size without following links;
  - unlinks links instead of following them;
  - never reads file content.

  Recursive SFTP deletes also compare each directory's planning-time mtime on first use. FTP deletes only files and empty directories, because FTP listings cannot reliably mark links. Remote names become local paths only through `FileActions.LocalName`/`LocalChild`.
- `ssh_fixture.py` lists directories with `stat` (it follows links) unless `FIXTURE_LSTAT_LIST=1`. Set that variable for any test that involves links, so listings match OpenSSH.
- Studio terminals open with flow control (`SharedSshSession.OpenShellAsync(…, flowControl: true)`, `TelnetPtyConnection.ConnectAsync(…, flowControl: true)`). The server may be 2 MiB ahead and is then held back. Without the flag, which is what `Forterm.Desktop` uses, a backlog of 128 blocks closes the terminal channel for good, and that old behaviour is covered by `TransportTests`. Work added to the terminal's reader (`OutputReceived`, which Studio raises on the read task) must stay short, and must never wait without a limit.
- Anything that types into a Studio terminal after a question must re-check the tab and `SessionId` first. Every paste goes through `PasteFilter`/`PasteGuard`.
- `SessionRecorder` converts and writes on its own thread. `TerminalTranscript` follows the rows of the current line, so it needs the terminal's column count with every chunk.
- `ssh_fixture.py` has a `flood` command (6 MiB of `X`) and `flood-past-window` (a server that ignores the channel window).
- A crashed test process still prints `Passed!` for the tests that had finished. When you filter `dotnet test` output, also look for `Catastrophic` and check the total.
- Studio interaction rules (alpha.7):
  - **Keyboard focus.** Avalonia has one focus for the whole process, so an element focused by code receives what the user is typing in a dialog or another window. Move focus only through `Services/KeyboardFocus` (`FocusTerminal` and `FocusText` already check it). A controller launch must never take the keyboard: raise `MainWindowViewModel.FocusRequested` only for something the user did, and pass the workspace it is for.
  - **Shortcuts** live in `Services/Shortcuts.cs` and are handled by `MainWindow` before anything else sees them. Off macOS never use Ctrl+Alt (it is AltGr), Ctrl+letter, or Ctrl+Shift with a digit or `-` (Ctrl+_ is undo). Zoom uses the vendored terminal's own gestures. A held key acts once (`IsRepeat`).
  - **Command palette** entries act only on the session that was shown when it opened; it closes by itself when `Selected` or `Workspaces` changes. `Selected` ignores workspaces that are no longer open.
  - **Motion.** Transitions and animations are defined only in `Themes/Motion.axaml`, which `Services/Motion.cs` installs or removes as a whole. They may change opacity, transforms and colours, never a terminal's size or what an action does. Tests run without them unless they call `Motion.Apply`. Keyframes cannot animate a `RenderTransform` that uses CSS transforms; use `Opacity` or `TranslateTransform`. A looping animation needs a selector that ends it (`BusyRing[IsActive=True]`).
  - **Layout traps.** `ScrollViewer.Padding` is ignored while measuring, so put a margin on the content. `StackPanel` gives a row more width than there is; use `RowsPanel`.
- Studio editor colours: `Syntax/*.xshd` name roles (`Comment`, `Key`, …) and `EditorSyntax` gives them the colours of the terminal scheme. File names and contents come from servers, so every repetition in a rule must be bounded (`{0,200}`, not `*`), and the editor uses `GuardedColorizer` instead of `TextEditor.SyntaxHighlighting`. `ColouringAFileCannotHoldUpTheWindow` times all languages on hostile lines.
- Studio terminal input (alpha.8): everything that is typed into a terminal by code goes through `TerminalTabViewModel.WriteAsync` (`IdleWatch`, one semaphore per terminal), never to a connection's `WriterStream` directly. With `flowControl` the stream is Core's `ShellWriter`, which sends straight into the channel (`ShellStream.SendAvailable`); SSH.NET's own `ShellStream.Write`/`Flush` keeps what timed out in its buffer and sends all of it again with the next write.
- "空闲保活" (`Services/IdleKeeper.cs`) is the only thing that types by itself into controller-launched sessions. It is off by default, a beat goes out at once or not at all (`IImmediateInput.TrySend`), and Enter is sent only at an empty prompt (`StudioTerminalControl.ReadPlace`) with nothing printed since the previous tick. Only what the user types counts as the user (`byUser`, `KindOf`).
- The file tree shows `WorkspaceViewModel.SelectedNode`; it does not own it. `null` from the tree is never a choice. `SelectNothing` means the top; `Rest` means "in this directory, no row selected". Assert selections after layout (`Settle`), and with a second session in front.
- Studio editor find (`EditorFindViewModel`): patterns run with `NonBacktracking` and a time limit, on text whose CRLF is one character (`Lines`).
- `StudioBackup`: a restore that is not marked as one's own must not switch on anything that sends, starts or lowers a guard; new preferences of that kind go into the untrusted list in `Plan`.
- Studio launches (alpha.9): `InstanceBroker.StartAsync(…, queue: true)` always keeps one more pipe open, so entries that start at the same moment are all taken; `Listen` without it stays as it is for `Forterm.Desktop`. A launch whose entry has already given up (exit code 4, the user was told) is not opened. `ControllerAccess.Shared` remembers wake-ups in memory, never their arguments. The 3 s dedup ignores the title.
- Windows installer: `packaging/studio-installer.nsi`, built by `tooling/package-studio-windows.py <version>` from the published ZIP (refuses to overwrite). It must only replace and remove files it installed (`taken-files.nsh`, `uninstall-files.nsh` are generated lists), never end a running program, and touch the registry and shortcuts only for the copy that is registered. `tooling/check-studio-installer.ps1` runs it for real and is for CI only. `tooling/native-check.py` opens the packaged program with a real window through its entries (macOS and Windows; own data directory, several loopback servers).
- Studio monitor: everything parsed from a server is bounded in `MonitorProbe.Parse` (`MostKilobytes`, the limit in `D`). `MonitorFindings.Average` weights each reading by the time it covers. A failed reading ends refreshing by itself until the user asks (`RefreshByUserAsync`).
- `ssh_fixture.py` keeps permission bits by path where there are none (Windows, or `FIXTURE_EMULATE=1` anywhere, which is how that path is tested on macOS). It must not use POSIX-only calls outside that switch.
- `ssh_fixture.py`: `FIXTURE_HOLD=1` keeps the shell from reading until the command `release-shell`, `FIXTURE_WINDOW=n` sets its window, and `close-shell` ends the last shell that was opened.
- `ssh_fixture.py` refuses every SSH `env` request unless `FIXTURE_ACCEPT_ENV=NAME,NAME` lists it. Its `stats` reply includes `env` (each request and whether it came before the shell) and `negotiated` (the server-side kex, host key and cipher).
- In a sandboxed shell, `TMPDIR` may be empty or too long. .NET named mutexes and pipes on macOS need a short `TMPDIR`, so run tests with `TMPDIR=$(getconf DARWIN_USER_TEMP_DIR)`.
- Some tooling defaults are stale. `publish.sh` defaults to `FORTERM_BUILD_TAG=alpha33`, so always pass the tag. `package-checkpoint.py` hard-codes a `-20261111` date suffix in the checkpoint directory name. Check both before packaging a new alpha.
- Restores use lock files (`RestorePackagesWithLockFile`, `--locked-mode` in publish). Adding or bumping a package means updating each project's `packages.lock.json`.
- Protocol tests start Python loopback servers: `tooling/ssh_fixture.py` (paramiko), `ftp_fixture.py` (pyftpdlib) and `agent_fixture.py`. They run under `tooling/fixture-venv/bin/python3`, with pins in `tooling/fixture-requirements.lock.txt`. Fixture behavior is toggled with `FIXTURE_*` env vars set by `TransportTests.Fixture`. The servers bind only to 127.0.0.1.
- UI tests use `Avalonia.Headless` with Skia (`TestApp` in `UiTests.cs`). They write screenshots to `avalonia/evidence/ui-<version>/`. These are software-rendered images, not native-window or Windows verification.
- `FORTERM_AVALONIA_DATA` overrides the data directory, which is also part of the per-user IPC pipe name. Use it to isolate manual runs.

### Electron (0.1.7): run from the repo root

```sh
npm ci
npm run dev            # esbuild main/preload → dist/, Vite dev server 127.0.0.1:5173, launches electron
npm run dev -- --diagnose -url 'ssh://ticket:password@localhost:2222'
npm run typecheck
npm test               # tsx --test tests/*.test.ts (node:test + in-process ssh2/FTP fixtures)
npx tsx --test tests/launch.test.ts                          # single file
npx tsx --test --test-name-pattern "regex" tests/sessions.test.ts
npm run build          # required before the Playwright smokes below (they launch `electron .`)
npm run test:ui | test:three-columns | test:navigation | test:reachability | test:burst
npm run format         # prettier
npm run dist:win       # build + Go launchers + electron-builder (NSIS/portable/zip)
```

`test:workspace`, `test:layout`, `test:lifecycle` and `test:linked-tabs` are aliases of `test:three-columns`. Set `FORTERM_PACKAGED_EXE` to run the smokes against a packaged exe, as CI does in `.github/workflows/windows.yml`. Set `FORTERM_TEST_DATA` to isolate userData.

### Go native (0.2.x): run from `native/`

```sh
go test ./internal/...
go test ./internal/sshconn -run TestName
go test -race ./internal/...
go build -o forterm-native ./cmd/forterm
CC="$PWD/scripts/clang-mingw.sh" ./scripts/build-windows.sh
```

`cmd/*-smoke` are synthetic local smoke programs and are not compiled into the app. `go.mod` replaces several deps with locally patched copies under `third_party/`.

## Architecture (shared across all three generations)

The same invariants are re-implemented in each stack. Keep them when changing behavior:

- **Four entry points.** `forterm` (main UI), `xshell` (SSH), `xftp` (SSH/SFTP URL → SFTP, `ftp://` stays real FTP) and `forterm-diagnose` (redacted argument report only, no network). The compatibility entries only forward argv as an array to the main app, never through a shell. A second instance hands its args to the running primary over a per-user IPC channel. Options are case-insensitive, and the URL is percent-decoded exactly once. Unknown arguments produce a redacted diagnostic. They are never silently ignored.
- **Launch dedup.** Identical controller launches within 3 s collapse into one session. The comparison uses an HMAC with a per-process random key, and the dedup lives in the shared core, not the launchers.
- **One authenticated SSH transport per session**, shared by the terminal PTY, SFTP, the exec channel (monitor, sudo chmod) and jump `direct-tcpip`. Each channel holds a lease or refcount, so closing one view never closes another. A channel the bastion denies is reported and the others keep working. **Never auto-reconnect, re-authenticate, or replay temporary tickets.** After a disconnect the user goes back to the controller.
- **Nothing is ever injected into the interactive terminal.** Keepalive uses SSH protocol want-reply (default 15 s, configurable 5–600 s or off) plus read-only probes on SFTP channels that are already open. Monitoring runs fixed read-only Linux `/proc`/`df` commands on a separate exec channel. Resize changes the PTY size only.
- **File sources are linked to a workspace explicitly**, never auto-matched by host, IP or username. Once a file-protocol session is linked, its top-level tab is hidden. It reappears when the last link is removed. Drafts and transfers stay bound to their original source.
- **Safe remote edit.** UTF-8 files of at most 1 MiB, at most 12 drafts. BOM, LF/CRLF and a missing trailing newline are preserved, and mixed line endings stay read-only until explicitly converted. Saving writes a temp file in the same directory, re-checks the hash and metadata, then renames. It never deletes and then renames. New files are created exclusively. FTP has no safe atomic replace, so the code refuses where it cannot guarantee safety.
- **Transfers** run in a serial queue per file connection. Cancel or failure stops the remaining items, with no automatic retry.
- **Secrets** live only in the OS store (Electron `safeStorage`, macOS Keychain, Windows Credential Manager), with no plaintext fallback. Exports never contain secrets. TOTP is local-only.
- **Bastion portal login and asset API are not implemented.** They wait on a vendor contract (`docs/BASTION-CONTRACT-TEMPLATE.md`, `docs/BASTION-ACCESS-DESIGN.md`). Don't invent endpoints, and don't add portal requests or fake assets.

### Avalonia layout (`avalonia/src`)

- `Forterm.Core`: protocols and domain logic with no UI. `SharedSshSession` owns the single transport, holds leases and never reconnects. Also here: SFTP/FTP sources, operations and streaming copy (32 KiB buffers), `RemoteText*` (safe edit), `TransferQueue`, `LaunchPlan`/`InstanceBroker` (argument parsing, dedup, IPC), `KnownHosts`, `SecretVault`, `Totp`, `LinuxMetrics`, `JumpProxy`, `PermissionExec`/`SudoPermissions` (with `permission_helper.py` as an embedded resource), and `TelnetPtyConnection`. `InternalsVisibleTo` exposes it to `Forterm.Tests`.
- `Forterm.Desktop`: the UI is built **in C# code** (only `App.axaml` is XAML). `MainWindow` hosts the connection library and tabs. `SessionPane` is one workspace with four collapsible regions: files on the left, terminal and editor in the center, monitor on the right. `ConnectionCoordinator` drives connect, fingerprint and credential flows. `ProfileStore` holds atomic metadata files.
- `Forterm.Launcher` (xshell/xftp), `Forterm.Diagnose`, `Forterm.Portable` (C# single-file exe that embeds the release ZIP).
- `vendor/`: patched copies of SSH.NET, XTerm.NET and Iciclecreek.Terminal. SSH.NET is patched so channel-only clients can borrow a transport; the terminal libraries carry resize and launch fixes. Each has a `FORTERM*` patch or notes file plus a `FortermPatchId` assembly attribute. Keep those notes in step with any vendor change.
  - SSH.NET has no patch-id attribute. Its record is `vendor/patches/sshnet-shared-transport.patch`, one `diff -u --label upstream/<file> --label forterm/<file>` section per changed file against upstream tag 2026.0.0 (`vendor/SSH.NET/UPSTREAM.json`). To update a section, rebuild the upstream copy by reverse-applying that section, then regenerate it with the same labels.
- The C# style is dense, often with many members or statements per line. Match the file you are editing.

### Electron layout (repo root)

- `src/main`: the Node main process. `sessions.ts` (ssh2 + node-pty), `transfers.ts`/`file-queue.ts`, `file-workspaces.ts`, `store.ts` (`vault.json` in userData), `launch.ts` + `launch-gate.ts` (parse + dedup), `bastion-*`. esbuild bundles it to `dist/main/index.cjs`, with `ssh2` and `node-pty` external.
- **IPC contract:** `src/shared/types.ts` defines `FortermAPI`. `src/preload/index.ts` exposes it via `contextBridge`, mapping each method to `ipcRenderer.invoke("forterm:<name>")`. `src/main/index.ts` registers each method with `handle(name, fn)`. `handle` checks that the sender is a known main-frame window and sanitizes errors: only static Chinese messages without `://` reach the renderer, and everything else becomes a generic message. Validate inputs with the zod schemas in `src/main/schema.ts`. Adding an API means touching all three files.
- `src/renderer`: the React app. `App.tsx` is the hub. Vite root is `src/renderer`, output `dist/renderer`.
- `launcher/main.go`: tiny Windows launchers built by `scripts/launchers.mjs`.

## Release and versioning process (Avalonia)

- To bump a version, update `<Version>` in `avalonia/Directory.Build.props` and the hard-coded strings in `src/Forterm.Desktop/Program.cs` (the `--runtime-check` JSON) and `MainWindow.cs` (the title). Also bump the default data realm `Forterm/AvaloniaAlphaNN` in `ProfileStore.cs`. Each alpha gets its own realm, so builds never read an older build's config.
- Each alpha ships with:
  - `docs/AVALONIA-ALPHA<N>-DELIVERY.md`
  - a new entry at the **top** of `docs/PROGRESS.md` and `docs/DEVELOPMENT-HANDOFF.md`
  - updates to `avalonia/README.md` and the capability matrix in `avalonia/docs/MIGRATION.md`

  The notes record package SHA256s, the test count, and exactly what was verified and what was not (Windows/ConPTY/DPI/IME, the OS vault and file picker, and real bastion use are still pending in the field). Don't claim verification that didn't happen. Headless screenshots and loopback fixtures are not native-window or production validation. Keep any failed runs in the record rather than dropping them.
- `checkpoints/`, `release/`, `avalonia/artifacts/` and `test-results/` are gitignored build outputs. **Never overwrite or modify frozen checkpoints or earlier release packages.** Produce a new version instead.
- Tests and tooling must never contact production SSO or bastion systems, reuse historical tickets, or touch the user's own SSH agent or config.
