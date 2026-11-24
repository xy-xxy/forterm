using System.Text.Json;
using Forterm.Studio.Services;

namespace Forterm.Studio.Library;

/// <summary>What a restore takes from a backup.</summary>
public sealed record RestoreChoice(bool Settings, bool QuickCommands, bool Connections, bool Trusted);

/// <summary>What a restore would do, or has done.</summary>
public sealed record RestoreSummary(bool Settings, int QuickCommands, int QuickKnown, int QuickLeft, int Groups, int Connections, int ConnectionsKnown, int Tunnels, int TunnelsLeft, int Stripped);

/// <summary>"备份恢复": settings, quick commands, connections, groups and tunnels in one file. Never passwords, keys'
/// passphrases, TOTP seeds, session environment variables or host fingerprints. A file can come from anybody, so a
/// restore adds what is missing instead of replacing connections, and leaves out what would run by itself or change
/// where this computer writes — unless the user says the backup is their own.</summary>
public sealed class StudioBackup
{
    public const string Format = "forterm-studio-backup";
    const long MaxBytes = 24 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    sealed record FileContent(string Format, int Version, DateTimeOffset Created, string App, StudioPreferences? Preferences, List<QuickCommand>? QuickCommands, List<ProfileGroup>? Groups, List<StudioProfile>? Profiles, List<TunnelDefinition>? Tunnels);

    readonly FileContent content;
    StudioBackup(FileContent content) => this.content = content;

    public DateTimeOffset Created => content.Created;
    public string App => content.App;
    public bool HasSettings => content.Preferences != null;
    public int QuickCommandCount => content.QuickCommands!.Count;
    public int ConnectionCount => content.Profiles!.Count;
    public int TunnelCount => content.Tunnels!.Count;

    // ---------- writing ----------

    public static byte[] Create(StudioPreferences preferences, QuickCommandStore quickCommands, LibraryStore library, DateTimeOffset now) => JsonSerializer.SerializeToUtf8Bytes(new FileContent(
        Format, 1, now, StudioPaths.Version, preferences.Validated(), quickCommands.Commands.ToList(), library.Groups.ToList(),
        library.Profiles.Select(p => p with { HasPassword = false, HasPassphrase = false, LastUsed = null, UseCount = 0, SessionEnvironment = [] }).ToList(), library.Tunnels.ToList()), Json);

    /// <summary>To a new file, or over one the user has chosen in the file dialog; never half written.</summary>
    public static void Write(string path, byte[] bytes)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // ---------- reading ----------

    public static StudioBackup Read(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("备份文件超过 24 MiB");
        byte[] bytes = File.ReadAllBytes(path);
        using (var doc = LibraryExchange.Parse(bytes, "备份文件"))
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format)
                throw new InvalidDataException("不是 Forterm Studio 的备份文件");
        FileContent? file;
        try { file = JsonSerializer.Deserialize<FileContent>(bytes, Json); }
        catch (JsonException) { throw new InvalidDataException("备份文件的内容不完整或类型不对"); }
        if (file is not { Version: 1 }) throw new InvalidDataException("备份文件的版本不受支持");
        file = file with
        {
            App = file.App is { Length: <= 64 } app && !app.Any(char.IsControl) ? app : "",
            QuickCommands = file.QuickCommands ?? [], Groups = file.Groups ?? [], Profiles = file.Profiles ?? [], Tunnels = file.Tunnels ?? [], Preferences = file.Preferences?.Validated(),
        };
        if (file.Profiles!.Count > LibraryStore.MaxProfiles || file.Groups!.Count > LibraryStore.MaxGroups || file.Tunnels!.Count > 200 || file.QuickCommands!.Count > QuickCommandStore.MaxCommands)
            throw new InvalidDataException("备份文件里的项目过多");
        try
        {
            foreach (var c in file.QuickCommands) QuickCommandStore.Validate(c);
            // The whole as it stands has to be a library: ids, groups, chains, tunnels.
            new LibraryStore(null).AddRange(file.Groups, file.Profiles.Select(p => p with { SessionEnvironment = p.IsSsh ? p.SessionEnvironment ?? [] : [] }).ToList(), file.Tunnels);
        }
        catch (Exception e) when (e is NullReferenceException or ArgumentNullException) { throw new InvalidDataException("备份文件含缺失或为空的字段"); }
        return new StudioBackup(file);
    }

    // ---------- restoring ----------

    static bool Same(StudioProfile a, StudioProfile b) => a.Protocol == b.Protocol && a.Port == b.Port && a.User == b.User && a.Name == b.Name && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
    static bool Same(QuickCommand a, QuickCommand b) => a.Name == b.Name && a.Text == b.Text && a.Enter == b.Enter;

    /// <summary>What restoring would do.</summary>
    public RestoreSummary Preview(RestoreChoice choice, QuickCommandStore quick, LibraryStore library) => Plan(choice, quick, library).Summary;

    sealed record Changes(RestoreSummary Summary, List<QuickCommand> Quick, List<ProfileGroup> Groups, List<StudioProfile> Profiles, List<TunnelDefinition> Tunnels, StudioPreferences? Preferences);

    Changes Plan(RestoreChoice choice, QuickCommandStore quick, LibraryStore library)
    {
        int stripped = 0;
        // ---- settings
        StudioPreferences? preferences = null;
        if (choice.Settings && content.Preferences is { } saved)
        {
            var now = Appearance.Current;
            preferences = choice.Trusted ? saved : saved with
            {
                // What sends by itself, a program that is started, and where files are put: not from just any file.
                IdleKeepAlive = now.IdleKeepAlive, IdleMinutes = now.IdleMinutes, IdleInput = now.IdleInput, IdleHours = now.IdleHours, IdleFullScreen = now.IdleFullScreen,
                LocalShell = now.LocalShell, LocalShellArgs = now.LocalShellArgs, LocalTerminalType = now.LocalTerminalType, DownloadFolder = now.DownloadFolder,
                // Guards stay up, warnings on, and keys where they are.
                Thresholds = now.Thresholds, Shortcuts = now.Shortcuts,
                ConfirmPaste = now.ConfirmPaste || saved.ConfirmPaste, RightClickMenu = now.RightClickMenu || saved.RightClickMenu, CopyOnSelect = now.CopyOnSelect && saved.CopyOnSelect,
            };
            if (preferences != saved) stripped++;
        }
        // ---- quick commands
        var newQuick = choice.QuickCommands ? content.QuickCommands!.Where(c => !quick.Commands.Any(x => Same(x, c))).Select(c => c with { Id = StudioProfile.NewId() }).ToList() : [];
        int quickKnown = choice.QuickCommands ? content.QuickCommands!.Count - newQuick.Count : 0, quickLeft = Math.Max(0, quick.Commands.Count + newQuick.Count - QuickCommandStore.MaxCommands);
        if (quickLeft > 0) newQuick.RemoveRange(newQuick.Count - quickLeft, quickLeft);
        // ---- connections
        var groups = new List<ProfileGroup>();
        var profiles = new List<StudioProfile>();
        var tunnels = new List<TunnelDefinition>();
        int known = 0, left = 0;
        if (choice.Connections)
        {
            // Groups by their path: one that is there is used, not doubled.
            var groupMap = new Dictionary<string, string> { [""] = "" };
            string GroupFor(string id)
            {
                if (groupMap.TryGetValue(id, out string? done)) return done;
                var g = content.Groups!.First(x => x.Id == id);
                string parent = GroupFor(g.ParentId);
                var there = library.Groups.Concat(groups).FirstOrDefault(x => x.ParentId == parent && x.Name == g.Name);
                if (there == null) groups.Add(there = new ProfileGroup(StudioProfile.NewId(), g.Name, parent));
                return groupMap[id] = there.Id;
            }
            foreach (var g in content.Groups!) GroupFor(g.Id); // also the empty ones
            var idMap = new Dictionary<string, string>();
            foreach (var p in content.Profiles!)
                if (library.Profiles.FirstOrDefault(x => Same(x, p)) is { } there) { idMap[p.Id] = there.Id; known++; }
                else idMap[p.Id] = StudioProfile.NewId();
            foreach (var p in content.Profiles.Where(p => library.Find(idMap[p.Id]) == null))
            {
                var restored = p with
                {
                    Id = idMap[p.Id], GroupId = GroupFor(p.GroupId), JumpChain = p.JumpChain.Select(j => idMap[j]).ToArray(),
                    HasPassword = false, HasPassphrase = false, LastUsed = null, UseCount = 0, Created = DateTimeOffset.UtcNow, SessionEnvironment = [],
                };
                if (!choice.Trusted && (p.InitialScript.Length > 0 || p.KeepAliveCommand.Length > 0 || p.Cipher.Length > 0 || p.KeyExchange.Length > 0))
                {
                    stripped++;
                    restored = restored with { InitialScript = "", KeepAliveCommand = "", KeepAliveCommandMs = 0, Cipher = "", KeyExchange = "" };
                }
                profiles.Add(restored);
            }
            var ports = library.Tunnels.Select(t => t.LocalPort).ToHashSet();
            foreach (var t in content.Tunnels!)
            {
                if (library.Tunnels.Any(x => x.ProfileId == idMap[t.ProfileId] && x.Kind == t.Kind && x.LocalPort == t.LocalPort && x.RemoteHost == t.RemoteHost && x.RemotePort == t.RemotePort)) continue;
                if (!ports.Add(t.LocalPort) || library.Tunnels.Count + tunnels.Count >= 200) { left++; continue; }
                tunnels.Add(t with { Id = StudioProfile.NewId(), ProfileId = idMap[t.ProfileId] });
            }
        }
        return new Changes(new RestoreSummary(preferences != null, newQuick.Count, quickKnown, quickLeft, groups.Count, profiles.Count, known, tunnels.Count, left, stripped), newQuick, groups, profiles, tunnels, preferences);
    }

    /// <summary>Connections first, in one write; then quick commands, in one write; then the settings, whose file
    /// is kept under another name. What fails leaves what came before it in place and is reported.</summary>
    public RestoreSummary Restore(RestoreChoice choice, QuickCommandStore quick, LibraryStore library, string? directory, DateTime now)
    {
        var plan = Plan(choice, quick, library);
        var done = new List<string>();
        try
        {
            if (plan.Profiles.Count + plan.Tunnels.Count + plan.Groups.Count > 0)
            {
                library.AddRange(plan.Groups, plan.Profiles, plan.Tunnels);
                done.Add($"{plan.Profiles.Count} 个连接");
            }
            if (plan.Quick.Count > 0)
            {
                quick.AddRange(plan.Quick);
                done.Add($"{plan.Quick.Count} 条快速命令");
            }
            if (plan.Preferences is { } preferences)
            {
                if (directory != null && Path.Combine(directory, "preferences.json") is var path && File.Exists(path))
                    File.Copy(path, Path.Combine(directory, $"preferences.before-restore-{now:yyyyMMdd-HHmmss-fff}.json"), overwrite: false);
                Appearance.Apply(preferences);
                Appearance.Save(directory, Appearance.Current);
            }
        }
        catch (Exception e) when (done.Count > 0) { throw new IOException($"{e.Message}。在此之前已添加 {string.Join("、", done)}，可以再次恢复其余部分", e); }
        return plan.Summary;
    }
}
