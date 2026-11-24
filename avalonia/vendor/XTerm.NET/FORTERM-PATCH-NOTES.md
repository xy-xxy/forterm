# Forterm resize patch

Pinned MIT source from NuGet3.0.0 commit453ed957885f2e87c777e9b4f0b88a3582065eab. Same assembly/API version; original file hashes and local diff in FORTERM-UPSTREAM.json/FORTERM.patch.

ReflowOnResize is opt-in-compatible (upstream default true), copied in options. Forterm live PTYs select false: keep physical rows stable while shell/TUI handles resize. Cropped cell tails remain in backing storage and return on grow; clear-right/full-display erases those tails respecting protection, erase-left does not. Normal grid logical width remains current cols; alternate screen retains upstream semantics. No client-side terminal clear or key/command injection.

Saved cursor now uses absolute buffer rows consistently with trim/reflow accounting. Resize anchors follow the logical cell/line rather than moving the prompt start for every inserted/deleted row; preserve pending-wrap position where applicable.

## Studio 0.4.0-alpha.6 search (search-newest-v16)

Additive, in Search/BufferSearch.cs and Search/SearchTypes.cs. `SearchOptions.KeepLast` (off by default) keeps the last MaxHits matches instead of the first. `MatchCount`/`CurrentMatch` count matches, not runs, so one that wrapped counts once. `Refresh` repeats the search and stays on the current match by position. A search follows the buffer that is shown: it re-subscribes to `Trimmed` when the terminal switched screens, and rows of the other buffer report no hits. `Count`, `CurrentIndex` and the default behaviour are unchanged.
