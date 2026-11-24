using System.Globalization;
using System.Text;
using Iciclecreek.Terminal;

namespace Forterm.Studio.Services;

/// <summary>Turns a terminal's output bytes into plain text lines for "会话记录". It follows the line the
/// cursor is on as the screen shows it — the rows it wraps over, carriage return, backspace, erasing and
/// moving between those rows — so an edited command line is recorded as it finally looked. Movement to
/// lines that are already finished is only approximated, and nothing is recorded while a full-screen
/// program (alternate screen) is showing.</summary>
public sealed class TerminalTranscript(Action<string> line)
{
    enum State { Text, Escape, EscapeOne, Csi, String, StringEscape }

    /// <summary>One screen row. The second column of a wide character is an empty cell.</summary>
    sealed class Row
    {
        public readonly List<string> Cells = new();
        /// <summary>Text went on in the row below because this one was full (xterm's "wrapped" mark), which
        /// happened at column <see cref="Full"/>. Erasing the end of the row takes the mark away.</summary>
        public bool Wraps;
        public int Full;
        public void Clear() { Cells.Clear(); Wraps = false; }
    }

    const int MaxCells = 32 * 1024, MaxParameters = 64, MaxString = 1024 * 1024;
    const int MinColumns = 2, MaxColumns = 1000, DefaultColumns = 80;
    public const string FullScreenNote = "[全屏程序的画面不记录]";

    readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
    readonly StringBuilder parameters = new();
    /// <summary>The rows of the line being written; more than one once it wrapped.</summary>
    readonly List<Row> rows = [new()];
    State state;
    /// <summary>The cursor. A column equal to <see cref="columns"/> means the row is full and the next
    /// character wraps.</summary>
    int row, column;
    int columns = DefaultColumns, skipped;
    char high;
    bool alternate, wrapOff;

    /// <summary>Starts in the middle of a session, on the line the screen already shows: a prompt, say, whose
    /// command is still to come.</summary>
    public void Begin(TerminalCursorLine shown, int columns)
    {
        this.columns = Math.Clamp(columns, MinColumns, MaxColumns);
        if (shown.Alternate)
        {
            alternate = true;
            line(FullScreenNote);
            return;
        }
        if (shown.Rows.Count == 0 || shown.Rows.Count * this.columns > MaxCells) return;
        rows.Clear();
        foreach (var cells in shown.Rows)
        {
            var next = new Row { Wraps = rows.Count < shown.Rows.Count - 1, Full = cells.Count };
            next.Cells.AddRange(cells);
            rows.Add(next);
        }
        row = Math.Clamp(shown.Row, 0, rows.Count - 1);
        column = Math.Clamp(shown.Column, 0, this.columns);
    }

    /// <summary>Takes output as it arrived, for a terminal <paramref name="columns"/> wide at that moment.</summary>
    public void Write(ReadOnlySpan<byte> bytes, int columns = DefaultColumns)
    {
        this.columns = Math.Clamp(columns, MinColumns, MaxColumns);
        column = Math.Min(column, this.columns);
        Span<char> chars = stackalloc char[1024];
        while (bytes.Length > 0)
        {
            decoder.Convert(bytes, chars, flush: false, out int used, out int count, out _);
            foreach (char c in chars[..count]) Next(c);
            bytes = bytes[used..];
        }
    }

    /// <summary>Emits the unfinished last line.</summary>
    public void Finish()
    {
        if (!alternate) EmitIfAny();
    }

    void Next(char c)
    {
        switch (state)
        {
            case State.Text: Text(c); break;
            case State.Escape: Escape(c); break;
            case State.EscapeOne: state = State.Text; break;
            case State.Csi: Csi(c); break;
            case State.String: String(c); break;
            case State.StringEscape:
                // ESC \ ends the string; any other ESC sequence ends it too and is handled itself.
                state = State.Text;
                if (c != '\\') Escape(c);
                break;
        }
    }

    void Text(char c)
    {
        switch (c)
        {
            case '\u001b': state = State.Escape; return;
            case '\u009b': parameters.Clear(); state = State.Csi; return;
            case '\u0090' or '\u0098' or '\u009d' or '\u009e' or '\u009f': skipped = 0; state = State.String; return;
            case '\r': column = 0; return;
            case '\n' or '\v' or '\f' or '\u0085': LineFeed(); return;
            case '\b': column = Math.Max(0, Math.Min(column, columns - 1) - 1); return;
            case '\t': if (column < columns) column = Math.Min(columns - 1, (column / 8 + 1) * 8); return;
        }
        if (char.IsControl(c)) return;
        if (char.IsHighSurrogate(c)) { high = c; return; }
        string text = char.IsLowSurrogate(c) ? high == 0 ? "�" : new string([high, c]) : c.ToString();
        high = '\0';
        Put(text);
    }

    void Put(string text)
    {
        if (alternate) return;
        int code = char.ConvertToUtf32(text, 0);
        var cells = rows[row].Cells;
        if (Joins(code))
        {
            // A combining mark belongs to the character before the cursor.
            for (int i = Math.Min(column, cells.Count) - 1; i >= 0; i--)
                if (cells[i].Length > 0) { if (cells[i].Length < 64) cells[i] += text; return; }
            return;
        }
        int width = Wide(code) ? 2 : 1;
        if (column + width > columns)
        {
            if (wrapOff) column = columns - width;
            else { Wrap(); cells = rows[row].Cells; }
        }
        while (cells.Count < column + width) cells.Add(" ");
        // Overwriting one half of a wide character leaves a blank in the other half.
        if (column > 0 && cells[column].Length == 0) cells[column - 1] = " ";
        if (column + width < cells.Count && cells[column + width].Length == 0) cells[column + width] = " ";
        cells[column] = text;
        if (width == 2) cells[column + 1] = "";
        column += width;
    }

    void Wrap()
    {
        rows[row].Wraps = true;
        rows[row].Full = Math.Min(column, columns);
        column = 0;
        if (row < rows.Count - 1) row++;
        else if ((rows.Count + 1) * columns > MaxCells) Emit(); // an endless line is cut
        else { rows.Add(new Row()); row++; }
    }

    void EmitIfAny() { if (rows.Any(r => r.Cells.Count > 0)) Emit(); }

    /// <summary>The line is finished: its rows become text, joined where they wrapped.</summary>
    void Emit()
    {
        int last = rows.Count - 1;
        while (last > row && rows[last].Cells.Count == 0) last--; // erased, and nothing written there since
        var text = new StringBuilder();
        for (int i = 0; i <= last; i++)
        {
            foreach (string cell in rows[i].Cells) text.Append(cell);
            if (rows[i].Wraps && i < last) { for (int n = rows[i].Cells.Count; n < rows[i].Full; n++) text.Append(' '); continue; }
            line(text.ToString().TrimEnd(' '));
            text.Clear();
        }
        rows.RemoveRange(1, rows.Count - 1);
        rows[0].Clear();
        // A line feed keeps the column; programs send CR LF. Keeping it here would indent every line of
        // output that came through a raw tty, which nobody wants in a log.
        row = column = 0;
    }

    void LineFeed()
    {
        if (alternate) return;
        if (row < rows.Count - 1) row++; // still inside the wrapped line
        else Emit();
    }

    void Up(int count) { row = Math.Max(0, row - count); column = Math.Min(column, columns - 1); }

    void Down(int count)
    {
        column = Math.Min(column, columns - 1);
        int inside = Math.Min(count, rows.Count - 1 - row);
        row += inside;
        if (inside == count) return;
        int keep = column;
        EmitIfAny();
        column = keep;
    }

    void Escape(char c)
    {
        state = State.Text;
        switch (c)
        {
            case '[': parameters.Clear(); state = State.Csi; break;
            case ']' or 'P' or 'X' or '^' or '_': skipped = 0; state = State.String; break;
            case '(' or ')' or '*' or '+' or '-' or '.' or '/' or '#' or '%' or ' ': state = State.EscapeOne; break;
            case 'D': LineFeed(); break;
            case 'E': LineFeed(); column = 0; break;
            case 'M': if (!alternate) Up(1); break;
            case 'c': if (!alternate) EmitIfAny(); alternate = wrapOff = false; column = 0; break;
            case '\u001b': state = State.Escape; break;
        }
    }

    void String(char c)
    {
        if (c is '\a' or '\u009c') state = State.Text;
        else if (c == '\u001b') state = State.StringEscape;
        else if (++skipped > MaxString) state = State.Text; // never terminated: do not swallow the session
    }

    void Csi(char c)
    {
        if (c == '\u001b') { state = State.Escape; return; }
        if (c is '\u0018' or '\u001a') { state = State.Text; return; }
        if (c < ' ') { Text(c); return; } // control characters act even inside a sequence
        if (c is < '@' or > '~') { if (parameters.Length < MaxParameters) parameters.Append(c); return; }
        state = State.Text;
        string p = parameters.ToString();
        if (c is 'h' or 'l' && p.StartsWith('?'))
        {
            var modes = p[1..].Split(';');
            if (modes.Contains("7")) wrapOff = c == 'l';
            if (modes.Any(m => m is "47" or "1047" or "1049")) Alternate(c == 'h');
            return;
        }
        if (alternate || p.Length > 0 && !char.IsAsciiDigit(p[0]) && p[0] != ';') return; // private or intermediate
        int n = Number(p, 0), count = Math.Max(1, n), at = Math.Min(column, columns - 1);
        var cells = rows[row].Cells;
        switch (c)
        {
            case 'A': Up(count); break;
            case 'B' or 'e': Down(count); break;
            case 'E': Down(count); column = 0; break;
            case 'F': Up(count); column = 0; break;
            case 'C' or 'a': column = Math.Min(columns - 1, at + count); break;
            case 'D': column = Math.Max(0, at - count); break;
            case 'G' or '`': column = Math.Min(columns, count) - 1; break;
            case 'K':
                if (n == 0) { Cut(cells, column); rows[row].Wraps = false; }
                else if (n == 1) Blank(cells, 0, at + 1);
                else rows[row].Clear();
                break;
            case 'X': Blank(cells, column, count); break;
            case 'P': if (column < cells.Count) cells.RemoveRange(column, Math.Min(count, cells.Count - column)); break;
            case '@':
                if (column >= cells.Count) break;
                cells.InsertRange(column, Enumerable.Repeat(" ", Math.Min(count, columns - column)));
                Cut(cells, columns); // pushed out of the row
                break;
            case 'J':
                if (n == 0)
                {
                    Cut(cells, column);
                    rows[row].Wraps = false;
                    foreach (var below in rows.Skip(row + 1)) below.Clear();
                }
                else if (n == 1)
                {
                    foreach (var above in rows.Take(row)) above.Clear();
                    Blank(cells, 0, at + 1);
                }
                else if (n == 2) EmitIfAny();
                break;
            case 'H' or 'f':
                EmitIfAny();
                column = Math.Min(columns, Math.Max(1, Number(p, 1))) - 1;
                break;
        }
    }

    static void Cut(List<string> cells, int from) { if (from < cells.Count) cells.RemoveRange(from, cells.Count - from); }

    static void Blank(List<string> cells, int from, int count)
    {
        for (int i = from; i < cells.Count && i < (long)from + count; i++) cells[i] = " ";
    }

    void Alternate(bool on)
    {
        if (on == alternate) return;
        if (on) EmitIfAny();
        alternate = on;
        column = 0;
        if (on) line(FullScreenNote);
    }

    /// <summary>The numeric parameter at <paramref name="index"/>; 0 when absent, bounded.</summary>
    static int Number(string parameters, int index)
    {
        var parts = parameters.Split(';');
        return index < parts.Length && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? Math.Min(value, MaxCells) : 0;
    }

    static bool Joins(int code) => code == 0x200D || CharUnicodeInfo.GetUnicodeCategory(code) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;

    /// <summary>The East Asian wide and emoji ranges that terminals draw two columns wide.</summary>
    static bool Wide(int c) => c >= 0x1100 && (c <= 0x115F || c is >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
        or >= 0xA000 and <= 0xA4CF or >= 0xA960 and <= 0xA97C or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE6F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6
        or >= 0x1F300 and <= 0x1F64F or >= 0x1F680 and <= 0x1F6FF or >= 0x1F900 and <= 0x1F9FF or >= 0x1FA70 and <= 0x1FAFF or >= 0x20000 and <= 0x3FFFD);
}
