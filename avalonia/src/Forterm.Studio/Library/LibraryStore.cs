using System.Text.Json;
using Forterm.Core;

namespace Forterm.Studio.Library;

/// <summary>Connection library metadata in <c>library.json</c>. Writes go to a temporary file in
/// the same directory and replace the original atomically, so a crash never leaves half a file.
/// Everything is validated on load and before save; nothing secret is ever written here.</summary>
public sealed class LibraryStore
{
    public const int MaxProfiles = 5000, MaxGroups = 1000, MaxGroupDepth = 8, MaxJumps = 4;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly string[] Protocols = ["ssh", "sftp", "ftp", "telnet"];
    static readonly string[] AuthModes = ["password", "keyboard", "key", "agent"];
    readonly string? path;
    LibraryDocument document = new();

    public LibraryStore(string? directory)
    {
        path = directory == null ? null : Path.Combine(directory, "library.json");
        if (path != null && File.Exists(path))
        {
            var info = new FileInfo(path);
            if (info.Length > 16 * 1024 * 1024) throw new IOException("连接库文件超过16MiB，未读取");
            document = Normalize(JsonSerializer.Deserialize<LibraryDocument>(File.ReadAllBytes(path), Json) ?? new LibraryDocument());
            Validate(document);
        }
    }

    public IReadOnlyList<StudioProfile> Profiles => document.Profiles;
    public IReadOnlyList<ProfileGroup> Groups => document.Groups;
    public IReadOnlyList<TunnelDefinition> Tunnels => document.Tunnels;

    public void UpsertTunnel(TunnelDefinition tunnel) => Commit(d =>
    {
        int index = d.Tunnels.FindIndex(t => t.Id == tunnel.Id);
        if (index >= 0) d.Tunnels[index] = tunnel; else d.Tunnels.Add(tunnel);
    });

    public void RemoveTunnel(string id) => Commit(d => d.Tunnels.RemoveAll(t => t.Id == id));
    public event Action? Changed;

    public StudioProfile? Find(string id) => document.Profiles.FirstOrDefault(p => p.Id == id);
    public ProfileGroup? FindGroup(string id) => document.Groups.FirstOrDefault(g => g.Id == id);

    public void Upsert(StudioProfile profile) => Commit(d =>
    {
        int index = d.Profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0) d.Profiles[index] = profile; else d.Profiles.Add(profile);
    });

    public void Remove(IReadOnlyCollection<string> ids) => Commit(d =>
    {
        d.Profiles.RemoveAll(p => ids.Contains(p.Id));
        d.Tunnels.RemoveAll(t => ids.Contains(t.ProfileId));
        // Profiles that used a removed connection as a jump keep the rest of their chain.
        for (int i = 0; i < d.Profiles.Count; i++)
            if (d.Profiles[i].JumpChain.Any(ids.Contains))
                d.Profiles[i] = d.Profiles[i] with { JumpChain = d.Profiles[i].JumpChain.Where(j => !ids.Contains(j)).ToArray() };
    });

    /// <summary>Adds groups, profiles and tunnels in one atomic write (import, restore).</summary>
    public void AddRange(IReadOnlyList<ProfileGroup> groups, IReadOnlyList<StudioProfile> profiles, IReadOnlyList<TunnelDefinition>? tunnels = null) => Commit(d =>
    {
        d.Groups.AddRange(groups);
        d.Profiles.AddRange(profiles);
        if (tunnels != null) d.Tunnels.AddRange(tunnels);
    });

    public void RecordUse(string id) => Commit(d =>
    {
        int index = d.Profiles.FindIndex(p => p.Id == id);
        if (index >= 0) d.Profiles[index] = d.Profiles[index] with { LastUsed = DateTimeOffset.UtcNow, UseCount = d.Profiles[index].UseCount + 1 };
    });

    public ProfileGroup AddGroup(string name, string parentId)
    {
        var group = new ProfileGroup(StudioProfile.NewId(), name.Trim(), parentId);
        Commit(d => d.Groups.Add(group));
        return group;
    }

    public void RenameGroup(string id, string name) => Commit(d =>
    {
        int index = d.Groups.FindIndex(g => g.Id == id);
        if (index >= 0) d.Groups[index] = d.Groups[index] with { Name = name.Trim() };
    });

    /// <summary>Removes the group; its connections and sub-groups move up to its parent.</summary>
    public void RemoveGroup(string id) => Commit(d =>
    {
        var group = d.Groups.FirstOrDefault(g => g.Id == id);
        if (group == null) return;
        d.Groups.Remove(group);
        for (int i = 0; i < d.Groups.Count; i++) if (d.Groups[i].ParentId == id) d.Groups[i] = d.Groups[i] with { ParentId = group.ParentId };
        for (int i = 0; i < d.Profiles.Count; i++) if (d.Profiles[i].GroupId == id) d.Profiles[i] = d.Profiles[i] with { GroupId = group.ParentId };
    });

    /// <summary>"YiXun / 子组" style path for display.</summary>
    public string GroupPath(string id)
    {
        var names = new List<string>();
        for (var g = FindGroup(id); g != null && names.Count <= MaxGroupDepth; g = FindGroup(g.ParentId)) names.Insert(0, g.Name);
        return string.Join(" / ", names);
    }

    void Commit(Action<LibraryDocument> change)
    {
        var next = Clone(document);
        change(next);
        Validate(next);
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
        document = next;
        Changed?.Invoke();
    }

    /// <summary>Load-time tolerance for preferences: an algorithm name this build does not offer (for
    /// example one a newer SSH library dropped) means "default order" instead of making the whole
    /// library unreadable. Nulls are left for <see cref="Validate"/> to report.</summary>
    static LibraryDocument Normalize(LibraryDocument d) => d.Profiles is null ? d : d with
    {
        Profiles = d.Profiles.Select(p => p is null ? p! : p with { Cipher = Known(p.Cipher, SshAlgorithms.Ciphers), KeyExchange = Known(p.KeyExchange, SshAlgorithms.KeyExchange) }).ToList(),
    };

    static string Known(string value, IReadOnlyList<string> names) => string.IsNullOrEmpty(value) || names.Contains(value) ? value : "";

    static LibraryDocument Clone(LibraryDocument d) => new() { Version = d.Version, Groups = d.Groups.ToList(), Profiles = d.Profiles.ToList(), Tunnels = d.Tunnels.ToList() };

    /// <summary>Hand-edited or damaged files can hold <c>null</c> where a list or text is expected;
    /// those are reported as invalid data, never as a crash.</summary>
    public static void Validate(LibraryDocument d)
    {
        try { ValidateDocument(d); }
        catch (Exception e) when (e is NullReferenceException or ArgumentNullException) { throw new InvalidDataException("连接库含缺失或为空的字段", e); }
    }

    static void ValidateDocument(LibraryDocument d)
    {
        if (d.Version != 1) throw new InvalidDataException("连接库版本不受支持");
        if (d.Profiles.Count > MaxProfiles) throw new InvalidDataException($"连接数超过 {MaxProfiles}");
        if (d.Groups.Count > MaxGroups) throw new InvalidDataException($"分组数超过 {MaxGroups}");
        var groupIds = new HashSet<string>();
        foreach (var g in d.Groups)
        {
            if (!ValidId(g.Id) || !groupIds.Add(g.Id)) throw new InvalidDataException("分组标识无效或重复");
            Text(g.Name, "分组名称", 1, 64);
        }
        foreach (var g in d.Groups)
        {
            if (g.ParentId.Length > 0 && !groupIds.Contains(g.ParentId)) throw new InvalidDataException("分组的上级不存在");
            int depth = 0;
            for (var p = g; p.ParentId.Length > 0; p = d.Groups.FirstOrDefault(x => x.Id == p.ParentId) ?? throw new InvalidDataException("分组的上级不存在"))
                if (++depth > MaxGroupDepth) throw new InvalidDataException("分组层级过深或形成循环");
        }
        var ids = new HashSet<string>();
        foreach (var p in d.Profiles)
        {
            if (!ValidId(p.Id) || !ids.Add(p.Id)) throw new InvalidDataException("连接标识无效或重复");
            ValidateProfile(p);
            if (p.GroupId.Length > 0 && !groupIds.Contains(p.GroupId)) throw new InvalidDataException("连接所在分组不存在");
        }
        foreach (var p in d.Profiles) ValidateChain(p, d.Profiles);
        if (d.Tunnels.Count > 200) throw new InvalidDataException("隧道最多 200 个");
        var tunnelIds = new HashSet<string>();
        foreach (var t in d.Tunnels)
        {
            if (!ValidId(t.Id) || !tunnelIds.Add(t.Id)) throw new InvalidDataException("隧道标识无效或重复");
            ValidateTunnel(t, d.Profiles);
        }
        foreach (var group in d.Tunnels.GroupBy(t => t.LocalPort).Where(g => g.Count() > 1))
            throw new InvalidDataException($"本地端口 {group.Key} 被多个隧道使用");
    }

    public static void ValidateProfile(StudioProfile p)
    {
        Text(p.Name, "名称", 0, 128);
        if (!Protocols.Contains(p.Protocol)) throw new InvalidDataException("协议不受支持");
        Text(p.Host, "地址", 1, 255);
        if (p.Host.IndexOfAny([' ', '\t', '/', '\\', '@']) >= 0) throw new InvalidDataException("地址不能包含空格、斜杠或 @");
        if (p.Port is < 1 or > 65535) throw new InvalidDataException("端口必须在 1–65535");
        Text(p.User, "用户名", p.Protocol == "telnet" ? 0 : 1, 128);
        if (!AuthModes.Contains(p.AuthMode)) throw new InvalidDataException("认证方式不受支持");
        if (p.Protocol is "ftp" && p.AuthMode != "password") throw new InvalidDataException("FTP 只支持密码认证");
        if (p.AuthMode == "key" && p.KeyPath.Trim().Length == 0) throw new InvalidDataException("私钥认证需要选择私钥文件");
        Text(p.KeyPath, "私钥路径", 0, 4096);
        Text(p.Note, "备注", 0, 2000, multiline: true);
        if (p.TimeoutMs != 0 && p.TimeoutMs is < 5000 or > 300000) throw new InvalidDataException("超时时间必须为 5000–300000 毫秒，或留空");
        if (p.HeartbeatMs != 0 && p.HeartbeatMs is < 5000 or > 600000) throw new InvalidDataException("心跳时间必须为 5000–600000 毫秒，或 0 关闭");
        if (!Encodings.IsSupported(p.Encoding)) throw new InvalidDataException("终端编码不受支持");
        try { SharedSshSession.ValidTerminalType(p.TerminalType); }
        catch (ArgumentException e) { throw new InvalidDataException(e.Message); }
        if (p.JumpChain.Count > MaxJumps) throw new InvalidDataException($"跳板最多 {MaxJumps} 级");
        if (p.JumpChain.Count > 0 && !p.IsSsh) throw new InvalidDataException("只有 SSH/SFTP 连接可以经过跳板机");
        if (p.Proxy.Length > 0) ProxyParser.Parse(p.Proxy);
        Text(p.DefaultPath, "默认路径", 0, 4096);
        if (p.DefaultPath.Length > 0 && !p.DefaultPath.StartsWith('/')) throw new InvalidDataException("默认路径必须是以 / 开头的绝对路径");
        Text(p.InitialScript, "初始执行", 0, 8192, multiline: true);
        Text(p.KeepAliveCommand, "保活命令", 0, 256);
        if (p.KeepAliveCommand.Length > 0 && p.KeepAliveCommandMs is < 5000 or > 3600000) throw new InvalidDataException("保活周期必须为 5000–3600000 毫秒");
        if (p.SessionEnvironment is null || p.SessionEnvironment.Any(e => e is null || e.Name is null || e.Value is null)) throw new InvalidDataException("会话环境变量含缺失或为空的字段");
        if (p.Cipher is null || p.KeyExchange is null) throw new InvalidDataException("算法设置含缺失的字段");
        if (!p.IsSsh && (p.SessionEnvironment.Count > 0 || p.Cipher.Length > 0 || p.KeyExchange.Length > 0))
            throw new InvalidDataException("只有 SSH/SFTP 连接可以设置算法和会话环境变量");
        try
        {
            ShellEnvironment.Validate(EnvironmentOf(p));
            SshAlgorithms.Validate(p.KeyExchange, p.Cipher);
        }
        catch (ArgumentException e) { throw new InvalidDataException(e.Message); }
    }

    public static IReadOnlyList<KeyValuePair<string, string>> EnvironmentOf(StudioProfile p) =>
        p.SessionEnvironment.Select(e => KeyValuePair.Create(e.Name, e.Value)).ToArray();

    public static void ValidateTunnel(TunnelDefinition t, IReadOnlyList<StudioProfile> profiles)
    {
        Text(t.Name, "隧道名称", 1, 64);
        Text(t.Note, "备注", 0, 500);
        var via = profiles.FirstOrDefault(p => p.Id == t.ProfileId) ?? throw new InvalidDataException("隧道经由的连接不存在");
        if (!via.IsSsh) throw new InvalidDataException("隧道只能经由 SSH 连接");
        if (t.LocalPort is < 1 or > 65535) throw new InvalidDataException("本地端口必须在 1–65535");
        if (t.Kind is not (TunnelDefinition.Local or TunnelDefinition.Dynamic)) throw new InvalidDataException("隧道类型不受支持");
        // A SOCKS proxy has no fixed target: leftover target fields are ignored, never fatal.
        if (t.IsDynamic) return;
        if (t.RemotePort is < 1 or > 65535) throw new InvalidDataException("远程端口必须在 1–65535");
        Text(t.RemoteHost, "远程地址", 1, 255);
        if (t.RemoteHost.IndexOfAny([' ', '\t', '/', '\\', '@']) >= 0) throw new InvalidDataException("远程地址无效");
    }

    static void ValidateChain(StudioProfile p, IReadOnlyList<StudioProfile> all)
    {
        if (p.JumpChain.Contains(p.Id)) throw new InvalidDataException("跳板链不能包含连接自身");
        if (p.JumpChain.Distinct().Count() != p.JumpChain.Count) throw new InvalidDataException("跳板链包含重复连接");
        foreach (string id in p.JumpChain)
        {
            var jump = all.FirstOrDefault(x => x.Id == id) ?? throw new InvalidDataException("跳板连接不存在");
            if (!jump.IsSsh) throw new InvalidDataException("跳板必须是 SSH 连接");
        }
    }

    static bool ValidId(string id) => id.Length is > 0 and <= 64 && id.All(char.IsAsciiLetterOrDigit);

    static void Text(string value, string field, int min, int max, bool multiline = false)
    {
        if (value.Length < min) throw new InvalidDataException(field + "不能为空");
        if (value.Length > max) throw new InvalidDataException($"{field}超过 {max} 个字符");
        foreach (char c in value)
            if (char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')) throw new InvalidDataException(field + "包含控制字符");
    }
}
