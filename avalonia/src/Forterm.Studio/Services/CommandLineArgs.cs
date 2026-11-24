using System.Text;

namespace Forterm.Studio.Services;

/// <summary>Splits the "默认程序参数" text into an argv array. Whitespace separates arguments; double
/// quotes group (an empty pair gives an empty argument); <c>\"</c> inside quotes is a literal quote.
/// Nothing else is interpreted: no variables, globs, pipes or other shell syntax, because the result
/// is handed to the program directly, never to a shell.</summary>
public static class CommandLineArgs
{
    public const int MaxArguments = 64;

    public static string[] Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return [];
        var current = new StringBuilder();
        bool inQuotes = false, started = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else current.Append(c);
            }
            else if (c == '"') { inQuotes = true; started = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (started) { result.Add(current.ToString()); current.Clear(); started = false; }
            }
            else { current.Append(c); started = true; }
        }
        if (inQuotes) throw new FormatException("参数中的引号没有闭合");
        if (started) result.Add(current.ToString());
        if (result.Count > MaxArguments) throw new FormatException($"参数最多 {MaxArguments} 个");
        return result.ToArray();
    }

    /// <summary>Split for display and launch: malformed text yields no arguments plus the reason.</summary>
    public static (string[] Args, string? Error) TrySplit(string? text)
    {
        try { return (Split(text), null); }
        catch (FormatException e) { return ([], e.Message); }
    }
}
