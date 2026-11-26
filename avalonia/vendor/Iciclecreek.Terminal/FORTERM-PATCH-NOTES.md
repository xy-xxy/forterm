# Forterm resize patch

Pinned MIT source from NuGet4.1.2 commit0851f5190296f315f0ca09e4ba1ffc3cc365227e, same assembly/API identity. Original file hashes and local diff in FORTERM-UPSTREAM.json/FORTERM.patch.

A newly attached PTY is immediately synchronized to the current grid. Async spawn rechecks layout after the await. Hidden/zero-area arrangements do not send transient one-cell geometry. Preserve requested resize policy even if template options arrive after emulator initialization. Existing monospace Nerd faces precede ordinary monospace in both property defaults and theme setter; no font installation. No client-side terminal input is generated for resize.

Model and PTY geometry are committed together after the current layout transaction, using final bounds. Intermediate arrange sizes do not trigger independent shell redraws; no backend-only debounce and no clear/input injection.

## alpha.16 startup readiness

Wait for OnInitialized before reading the emulator; coalesce concurrent explicit/Loaded launches. Workspace terminals opt out of automatic Loaded startup because their owner explicitly starts or attaches them. Cancel pending initialization on disposal, capture the launch cancellation token, and dispose a spawned process if ownership was lost during await. Genuine spawn exceptions remain visible; Release stderr records only exception type and stack, never argv/environment/message.

The alpha.15 control diff file was empty despite source changes. Alpha.16 regenerates the full diff against the exact upstream commit and verifies every previously recorded original hash. Older frozen records remain untouched.

## Studio 0.4.0-alpha.6 (review-highlight-v17)

Additions live in TerminalView.Forterm.cs, with forwarding members on TerminalControl. All hooks are null by default, which leaves upstream behaviour (and Forterm.Desktop) as it was.

- `PasteFilter`: every paste path of the view goes through it; the result is sent only to the process it was reviewed for. With a filter, paste MIME mode 5522 is not used.
- `CopyFilter`: adjusts selected text before it reaches the clipboard.
- `HighlightProvider`: colours text in the default foreground on the default background, normal screen only. Cells with colours of their own, inverse cells and reverse video are left alone.
- Scrollback search holds the terminal lock while it reads the buffer. `RefreshSearch`, `SearchMatchCount` and `SearchCurrentMatch` forward the XTerm.NET additions.
- `ReadCursorLine` and `OutputWritten`: the line the cursor is on, with the output position it belongs to, so a recording can start in the middle of a session without losing or repeating output.

## Studio 0.4.0-alpha.10 (paste-sender-v18)

- `PasteSender`: when set, a paste that `PasteFilter` let through is handed to the host instead of being written: the keys that delete a keyboard selection the paste replaces (or ""), the text, and whether the program asked for bracketed paste (the text is not wrapped yet). Studio uses it to send a long paste in pieces that can be stopped while the server does not read, and to close a bracket it opened. The view calls it while it holds its own input semaphore, so the paste stays in line with keys typed before and after it, on the caller's thread. Null by default: the view writes the paste itself, as before (and as Forterm.Desktop does).
