using System.Text.Json;
namespace Forterm.Desktop;
/// <summary>New versioned metadata only, independent of every older Forterm
/// config. Credentials cannot enter this serialized record type.</summary>
public sealed class ProfileStore
{
    readonly string? directory; public string? DirectoryPath => directory;
    public ProfileStore(string? directory) => this.directory = directory;
    public static string DefaultDirectory => Environment.GetEnvironmentVariable("FORTERM_AVALONIA_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forterm", "AvaloniaAlpha36");
    public ConnectionProfile[] Load()
    {
        if (directory == null || !File.Exists(Path.Combine(directory, "profiles.json"))) return [];
        var file = new FileInfo(Path.Combine(directory, "profiles.json")); if (file.Length > 4 * 1024 * 1024) throw new IOException("连接库超过4MiB边界");
        return JsonSerializer.Deserialize<ConnectionProfile[]>(File.ReadAllText(file.FullName)) ?? [];
    }
    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        if (directory == null) return; Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "profiles.json"); var temp = Path.Combine(directory, "profiles." + Guid.NewGuid().ToString("N") + ".tmp");
        try { var data = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }); if (System.Text.Encoding.UTF8.GetByteCount(data) > 4 * 1024 * 1024) throw new IOException("连接库超过4MiB保存边界，未覆盖旧库"); File.WriteAllText(temp, data); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
