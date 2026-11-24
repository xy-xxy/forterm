using System.Text;

namespace Forterm.Studio.Services;

/// <summary>What a paste would send. <see cref="Send"/> uses CR line ends (Enter), <see cref="Preview"/> uses LF.</summary>
public sealed record PasteReview(string Send, string Preview, int Lines, int Removed, bool EndsWithEnter)
{
    public bool HasLineBreak => Lines > 1 || EndsWithEnter;

    /// <summary>Multi-line text runs as it is pasted; removed characters mean the text is not what it looked like.</summary>
    public bool NeedsConfirmation => HasLineBreak || Removed > 0 || Send.Length > PasteGuard.LongText;

    /// <summary>One line for the dialog: what is being pasted and why it asks.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Lines} 行，{Send.Length} 个字符" };
            if (HasLineBreak) parts.Add("换行会被当作回车，命令可能立即执行");
            if (Removed > 0) parts.Add($"已去掉 {Removed} 个控制字符");
            return string.Join("；", parts) + "。";
        }
    }
}

/// <summary>Prepares clipboard text for a terminal. Line ends become CR, as Enter sends. Other control
/// characters are removed: an ESC in the text could end bracketed paste early or reprogram the terminal,
/// and ^C, ^U, backspace and the like act on the line editor. Bidirectional overrides are removed because
/// they make the preview read differently from what is sent. Tabs stay.</summary>
public static class PasteGuard
{
    public const int LongText = 4096;
    /// <summary>Larger clipboard text is refused: pasted into a shell it is almost certainly a mistake.</summary>
    public const int MaxLength = 1024 * 1024;

    /// <summary>Text copied out of a terminal: the grid pads every row with blanks, which are not part of
    /// what the program printed and would, for example, end up behind a line-continuation backslash.</summary>
    public static string Copied(string selection)
    {
        if (selection.Length == 0) return selection;
        string text = string.Join('\n', selection.Split('\n').Select(l => l.TrimEnd(' '))).TrimEnd('\n');
        return text.Length > 0 ? text : selection; // only blanks were selected: they are what is wanted
    }

    public static PasteReview Review(string clipboard)
    {
        var send = new StringBuilder(Math.Min(clipboard.Length, MaxLength));
        int removed = 0, breaks = 0;
        for (int i = 0; i < clipboard.Length; i++)
        {
            char c = clipboard[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < clipboard.Length && clipboard[i + 1] == '\n') i++;
                send.Append('\r');
                breaks++;
            }
            else if (c == '\t' || !IsRemoved(c)) send.Append(c);
            else removed++;
        }
        string text = send.ToString();
        bool ends = text.EndsWith('\r');
        return new PasteReview(text, text.Replace('\r', '\n'), breaks + (ends ? 0 : 1), removed, ends);
    }

    static bool IsRemoved(char c) => char.IsControl(c) || IsBidiControl(c);

    /// <summary>Embedding, override and isolate characters: text that displays in another order than it is sent.</summary>
    public static bool IsBidiControl(char c) => c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';
}
