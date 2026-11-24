using System.Text.Json;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed record UserPreferences
{
    public WorkspaceLayout Workspace { get; init; } = new();
    public string Appearance { get; init; } = "dark"; public double TerminalFontSize { get; init; } = 14; public double UiFontSize { get; init; } = 14;
    public bool NavigationVisible { get; init; } = true; public double NavigationWidth { get; init; } = 280; public bool AutoCollapseNavigation { get; init; } = true;
    public bool KeepAliveEnabled { get; init; } = true; public int KeepAliveSeconds { get; init; } = 15; public bool SftpProbeEnabled { get; init; } = true;
    public void Validate() { if (Appearance is not ("dark" or "light" or "system") || TerminalFontSize is < 10 or > 30 || UiFontSize is < 12 or > 20 || NavigationWidth is < 180 or > 420) throw new IOException("主题、字号或导航宽度超出边界"); if (Workspace == null) throw new IOException("工作区布局缺失"); Workspace.Validate(); SessionOptions().Validate(); }
    public SessionConnectOptions SessionOptions() => new() { KeepAliveEnabled = KeepAliveEnabled, KeepAliveSeconds = KeepAliveSeconds, SftpProbeEnabled = SftpProbeEnabled };
}
public sealed class PreferenceStore(string? directory)
{
    public UserPreferences Load() { if (directory == null) return new(); string path = Path.Combine(directory, "preferences.json"); if (!File.Exists(path)) return new(); if (new FileInfo(path).Length > 65536) throw new IOException("偏好文件超出64KiB边界"); var p = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path)) ?? new(); p.Validate(); return p; }
    public void Save(UserPreferences p) { p.Validate(); if (directory == null) return; Directory.CreateDirectory(directory); string target = Path.Combine(directory, "preferences.json"), temp = Path.Combine(directory, "preferences." + Guid.NewGuid().ToString("N") + ".tmp"); try { File.WriteAllText(temp, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true })); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
}
