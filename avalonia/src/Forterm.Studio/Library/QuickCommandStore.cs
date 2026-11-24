using System.Text.Json;

namespace Forterm.Studio.Library;

/// <summary>"快速命令": text the user types into a terminal with one click. Plain text on disk, so it is no
/// place for passwords (the settings page says so).</summary>
public sealed record QuickCommand
{
    public string Id { get; init; } = StudioProfile.NewId();
    public string Name { get; init; } = "";
    /// <summary>One or more lines, LF between them.</summary>
    public string Text { get; init; } = "";
    /// <summary>Press Enter after the last line. Off leaves a single line for the user to finish.</summary>
    public bool Enter { get; init; } = true;
    /// <summary>Show the command and ask before sending.</summary>
    public bool Confirm { get; init; }

    public string FirstLine => Text.Split('\n')[0];
    public int LineCount => Text.Split('\n').Length;
    /// <summary>What the list shows under the name.</summary>
    public string Preview => LineCount > 1 ? FirstLine + $" …（{LineCount} 行）" : FirstLine;
    /// <summary>What is sent: lines end with CR, as Enter does.</summary>
    public string Typed => Text.Replace('\n', '\r') + (Enter ? "\r" : "");
}

/// <summary>Quick commands, saved atomically. An unreadable file is never overwritten: loading throws and
/// the application runs with an in-memory list instead.</summary>
public sealed class QuickCommandStore
{
    public const int MaxCommands = 200, MaxName = 40, MaxText = 4000;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly string? path;
    List<QuickCommand> commands = new();

    public QuickCommandStore(string? directory)
    {
        path = directory == null ? null : Path.Combine(directory, "quick-commands.json");
        if (path == null || !File.Exists(path)) return;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("快速命令文件超过8MiB，未读取");
        var loaded = JsonSerializer.Deserialize<List<QuickCommand>>(File.ReadAllBytes(path), Json) ?? new();
        try { foreach (var c in loaded) Validate(c); }
        catch (Exception e) when (e is NullReferenceException or ArgumentNullException) { throw new InvalidDataException("快速命令文件含缺失或为空的字段", e); }
        if (loaded.Count > MaxCommands || loaded.DistinctBy(c => c.Id).Count() != loaded.Count) throw new InvalidDataException("快速命令过多或标识重复");
        commands = loaded;
    }

    public IReadOnlyList<QuickCommand> Commands => commands;
    public event Action? Changed;

    /// <summary>Line ends become LF; blank lines at the end are dropped, as they would only press Enter again.</summary>
    public static QuickCommand Normalize(QuickCommand c) => c with
    {
        Name = c.Name.Trim(),
        Text = c.Text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n'),
    };

    public void Upsert(QuickCommand command)
    {
        command = Normalize(command);
        Validate(command);
        var next = commands.ToList();
        int index = next.FindIndex(c => c.Id == command.Id);
        if (index >= 0) next[index] = command; else next.Add(command);
        if (next.Count > MaxCommands) throw new InvalidDataException($"快速命令最多 {MaxCommands} 条");
        Write(next);
    }

    /// <summary>Adds several in one write (restore).</summary>
    public void AddRange(IReadOnlyList<QuickCommand> added)
    {
        var next = commands.Concat(added.Select(Normalize)).ToList();
        foreach (var c in next) Validate(c);
        if (next.Count > MaxCommands) throw new InvalidDataException($"快速命令最多 {MaxCommands} 条");
        Write(next);
    }

    public void Remove(string id) => Write(commands.Where(c => c.Id != id).ToList());

    /// <summary>Moves a command one place up (-1) or down (+1); at the edge nothing happens.</summary>
    public void Move(string id, int by)
    {
        int from = commands.FindIndex(c => c.Id == id), to = from + by;
        if (from < 0 || to < 0 || to >= commands.Count) return;
        var next = commands.ToList();
        (next[from], next[to]) = (next[to], next[from]);
        Write(next);
    }

    void Write(List<QuickCommand> next)
    {
        if (path != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            try
            {
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(next, Json));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        commands = next;
        Changed?.Invoke();
    }

    public static void Validate(QuickCommand c)
    {
        if (c.Id.Length is 0 or > 64 || !c.Id.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException("快速命令标识无效");
        if (c.Name.Trim().Length is 0 or > MaxName || c.Name.Any(char.IsControl)) throw new InvalidDataException($"名称不能为空，最长 {MaxName} 个字符");
        if (c.Text.Trim().Length == 0) throw new InvalidDataException("命令内容不能为空");
        if (c.Text.Length > MaxText) throw new InvalidDataException($"命令内容最长 {MaxText} 个字符");
        if (c.Text.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\t') || Services.PasteGuard.IsBidiControl(ch)))
            throw new InvalidDataException("命令内容不能包含控制字符");
        if (!c.Enter && c.LineCount > 1) throw new InvalidDataException("不自动回车的命令只能有一行");
    }
}
