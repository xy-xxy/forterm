using System.Collections.ObjectModel;
using System.Globalization;
using Forterm.Studio.Mvvm;

namespace Forterm.Studio.Services;

/// <summary>A program that a controller starts instead of Xshell or Xftp.</summary>
public sealed record EntryProgram(string Title, string Purpose, string Path, bool IsThere)
{
    public string State => IsThere ? "已就绪" : "找不到这个文件：请保留整个文件夹，或重新安装";
}

/// <summary>One launch by a controller, as the settings show it. Nothing of its arguments but which entry it was.</summary>
public sealed class WakeUp(string entry, DateTimeOffset at) : ObservableObject
{
    string result = "正在打开…";
    bool failed;
    public string Entry { get; } = entry;
    public string Time { get; } = at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string Result { get => result; private set => Set(ref result, value); }
    public bool IsFailed { get => failed; private set => Set(ref failed, value); }
    public void End(string text, bool bad) { Result = text; IsFailed = bad; }
}

/// <summary>Where the entries for the controller are, and what came through them since the program was started. In memory
/// only.</summary>
public sealed class ControllerAccess
{
    public const int MostShown = 8;
    /// <summary>That of the program, whichever of its windows a launch came to.</summary>
    public static ControllerAccess Shared { get; } = new();
    /// <summary>Where this program is; the entries are next to it.</summary>
    public string Folder { get; init; } = AppContext.BaseDirectory;
    public EntryProgram[] Programs => Entries(Folder, OperatingSystem.IsWindows());
    public ObservableCollection<WakeUp> WakeUps { get; } = new();

    public WakeUp Began(string entry, DateTimeOffset at)
    {
        var wake = new WakeUp(entry, at);
        WakeUps.Insert(0, wake);
        while (WakeUps.Count > MostShown) WakeUps.RemoveAt(WakeUps.Count - 1);
        return wake;
    }

    public static EntryProgram[] Entries(string folder, bool windows)
    {
        EntryProgram Entry(string name, string title, string purpose)
        {
            string path = Path.Combine(folder, windows ? name + ".exe" : name);
            return new(title, purpose, path, File.Exists(path));
        }
        return
        [
            Entry("xshell", "Xshell 入口", "打开 SSH 终端"),
            Entry("xftp", "Xftp 入口", "打开文件传输（SFTP；ftp:// 打开 FTP）"),
        ];
    }
}
