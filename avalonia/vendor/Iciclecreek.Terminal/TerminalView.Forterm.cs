using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;
using XTerm.Buffer;
using XTerm.Common;

namespace Iciclecreek.Terminal
{
    /// <summary>A run of characters of one line to draw in <paramref name="Brush"/>. Positions are UTF-16
    /// indices into the text the provider was given.</summary>
    public readonly record struct TextHighlight(int Start, int Length, IBrush Brush);

    /// <summary>The line the cursor is on, as its screen rows: one text per column, empty for the second
    /// column of a wide character. <paramref name="Written"/> is <see cref="TerminalView.OutputWritten"/>
    /// at that moment.</summary>
    public sealed record TerminalCursorLine(IReadOnlyList<IReadOnlyList<string>> Rows, int Row, int Column, bool Alternate, long Written);

    // Forterm additions, kept apart from the upstream source. All are null by default, which leaves every
    // upstream behaviour as it was.
    public partial class TerminalView
    {
        private Func<string, IReadOnlyList<TextHighlight>>? _highlightProvider;

        /// <summary>
        /// Reviews clipboard text before <see cref="PasteAsync"/> sends it. Gets the raw text and returns
        /// what to send, or null to send nothing. Every paste path of this view goes through PasteAsync.
        /// </summary>
        /// <remarks>
        /// With a filter set the paste is always sent as text: the bracketed-paste MIME announcement
        /// (mode 5522) is not used, because it would hand the program formats the filter never saw.
        /// </remarks>
        public Func<string, Task<string?>>? PasteFilter { get; set; }

        /// <summary>
        /// Adjusts the selected text before <see cref="CopyAsync"/> puts it on the clipboard.
        /// </summary>
        public Func<string, string>? CopyFilter { get; set; }

        /// <summary>
        /// Colours for text the program printed in the default foreground. Called with the text of one
        /// physical row whenever that row is rebuilt; earlier spans win where spans overlap. Cells with a
        /// text or background colour of their own, inverse cells, the alternate screen and reverse video are left alone.
        /// </summary>
        public Func<string, IReadOnlyList<TextHighlight>>? HighlightProvider
        {
            get => _highlightProvider;
            set
            {
                if (ReferenceEquals(_highlightProvider, value))
                    return;
                _highlightProvider = value;
                InvalidateRunCaches();
            }
        }

        /// <summary>Bytes of output the emulator has taken in so far. <see cref="OutputReceived"/> is raised
        /// for a chunk just before that chunk is added.</summary>
        public long OutputWritten => System.Threading.Interlocked.Read(ref Counters.Bytes);

        /// <summary>The line the cursor is on, over all the rows it wraps across.</summary>
        public TerminalCursorLine ReadCursorLine()
        {
            lock (_terminalLock)
            {
                var buffer = _terminal.Buffer;
                int at = buffer.YBase + buffer.Y, first = at, last = at;
                while (first > 0 && at - first < _terminal.Rows && buffer.Lines[first] is { IsWrapped: true })
                    first--;
                while (last + 1 < buffer.Lines.Length && last - at < _terminal.Rows && buffer.Lines[last + 1] is { IsWrapped: true })
                    last++;

                var rows = new List<IReadOnlyList<string>>();
                for (int r = first; r <= last; r++)
                {
                    var cells = new List<string>();
                    if (buffer.Lines[r] is { } line)
                    {
                        int end = Math.Min(_terminal.Cols, line.Length);
                        // Blank cells at the end of the line are not text; inside a wrapped line they are.
                        while (r == last && end > 0 && line[end - 1] is { Width: <= 1, CodePoint: 0 or ' ' } && (r > at || end > buffer.X))
                            end--;
                        for (int x = 0; x < end; x++)
                            cells.Add(line[x] is var cell && cell.Width == 0 ? "" : cell.CodePoint == 0 ? " " : cell.Content);
                    }
                    rows.Add(cells);
                }
                return new TerminalCursorLine(rows, at - first, buffer.X, _terminal.IsAlternateBufferActive, OutputWritten);
            }
        }

        /// <summary>How many matches the search holds; one that wrapped counts once.</summary>
        public int SearchMatchCount
        {
            get { lock (_terminalLock) return _search?.MatchCount ?? 0; }
        }

        /// <summary>Index of the current match within <see cref="SearchMatchCount"/>, or -1.</summary>
        public int SearchCurrentMatch
        {
            get { lock (_terminalLock) return _search?.CurrentMatch ?? -1; }
        }

        /// <summary>Searches again for the same text, staying on the current match when it is still there.</summary>
        public int RefreshSearch()
        {
            int count = 0;
            lock (_terminalLock)
            {
                if (_search is { } search)
                {
                    count = search.Refresh();
                    _currentMatchId = search.CurrentMatchId;
                }
            }
            InvalidateVisual();
            return count;
        }

        /// <summary>The highlight brush of each column of a row, or null when nothing on it is highlighted.</summary>
        private IBrush?[]? HighlightColumns(BufferLine line)
        {
            var provider = _highlightProvider;
            if (provider is null || _terminal.ReverseVideo || _terminal.IsAlternateBufferActive)
                return null;

            int cols = Math.Min(_terminal.Cols, line.Length);
            int last = cols - 1;
            while (last >= 0 && line[last] is { Width: <= 1, CodePoint: 0 or ' ' })
                last--;
            if (last < 0)
                return null;

            var text = new StringBuilder(last + 1);
            var columns = new List<int>(last + 1);
            for (int x = 0; x <= last; x++)
            {
                var cell = line[x];
                if (cell.Width == 0)
                    continue;
                string content = cell.CodePoint == 0 ? " " : cell.Content;
                text.Append(content);
                for (int i = 0; i < content.Length; i++)
                    columns.Add(x);
            }

            IReadOnlyList<TextHighlight> spans;
            try { spans = provider(text.ToString()); }
            catch (Exception) { return null; } // a host callback must not cost the frame
            if (spans.Count == 0)
                return null;

            var result = new IBrush?[cols];
            foreach (var span in spans)
            {
                int end = (int)Math.Min(columns.Count, (long)span.Start + span.Length);
                for (int i = Math.Max(0, span.Start); i < end; i++)
                    result[columns[i]] ??= span.Brush;
            }
            return result;
        }

        /// <summary>The brush of a column; the grid may have grown since the row was read.</summary>
        private static IBrush? HighlightAt(IBrush?[]? highlights, int column) =>
            highlights is not null && column < highlights.Length ? highlights[column] : null;

        /// <summary>Whether a highlight may recolour this cell: only text in the default colours. On a
        /// background the program chose, the highlight could be the very same colour.</summary>
        private static bool TakesHighlight(BufferCell cell) =>
            cell.Attributes.GetFgColor() == 256 && cell.Attributes.GetBgColor() == 257 && !cell.Attributes.IsInverse();

        /// <summary>The filtered paste: see <see cref="PasteFilter"/>.</summary>
        private async Task PasteFilteredAsync(string? text, Func<string, Task<string?>> filter)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var session = SessionId;
            var review = filter(text);
            // A keyboard selection is replaced only when nothing could have happened in between: its
            // deletion is keystrokes counted from where the cursor was.
            bool immediate = review.IsCompletedSuccessfully;
            var reviewed = await review;

            // Reviewed for the process the user was looking at, not for one attached since.
            if (string.IsNullOrEmpty(reviewed) || _ptyConnection == null || SessionId != session)
                return;

            if (_terminal.BracketedPasteMode)
                reviewed = $"\u001b[200~{reviewed}\u001b[201~";
            await SendToPtyAsync((immediate ? TakeKeyboardSelectionDeletion() : string.Empty) + reviewed);
        }
    }
}
