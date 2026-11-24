Upstream: github.com/fyne-io/terminal 45431670057e (2026-10-23), original license retained.

Local integration changes: marshal output parsing onto Fyne UI thread; OnPaste hook and approved Paste method; normal non-mac Ctrl+V routes through hook; Ctrl+C with selection copies. These changes do not fix or claim complete wide-character VT semantics. No browser engine is included.


2026-10-27 verified corrections:
- Exit the read loop on every terminal read error; process final bytes before exit. This fixes closed io.Pipe error flooding.
- Parse and update the grid in one Fyne UI dispatch, with nested output dispatch removed to preserve VT ordering.
- Retain incomplete UTF-8 sequences across reads using owned buffer memory.
- Advance by display columns for CJK, reserve continuation cells, clear both halves on overwrite and wrap a wide glyph before a one-column margin.
- Native viewport renderer paints backgrounds before glyphs to prevent colored-cell backgrounds clipping wide glyphs. Selection excludes continuation cells.
- Fyne test driver runs dispatch inline: the probe waits for the next read boundary before inspecting/resizing the grid, preventing test-only concurrent UI access.

The focused regression covers Chinese text, cursor DSR, split UTF-8, half overwrite, right edge wrap, alternate screen, ANSI pixels, selection, Ctrl-C, arrows, paste and resize. Combining marks/emoji clusters remain a recorded limitation: no claim of full xterm.js compatibility.
- Windows ConPTY cleanup uses an idempotent closer, SystemRoot rather than hardcoded C:\\WINDOWS, and atomic exit status; shell wait errors no longer call log.Fatal and terminate unrelated sessions. Cross-compiled only; Windows runtime checks remain required.

The local viewport renderer deliberately does not add browser or WebView dependencies. It renders only visible terminal rows and keeps protocol parsing in the terminal package. Full Unicode grapheme and scrollback behavior still need separate implementation and tests.
