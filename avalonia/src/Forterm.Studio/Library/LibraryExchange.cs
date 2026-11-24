using System.Text.Json;

namespace Forterm.Studio.Library;

/// <summary>Studio connection export/import. Exports never contain secrets or "saved secret"
/// markers; imports get new identifiers, keep jump chains only within the imported set, and
/// never inherit credentials or host-key trust.</summary>
public static class LibraryExchange
{
    public const string Format = "forterm-studio-connections";
    const long MaxBytes = 4 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    sealed record ExportFile(string Format, int Version, List<ProfileGroup> Groups, List<StudioProfile> Profiles);

    public static byte[] Export(LibraryStore store, IReadOnlyCollection<string> profileIds)
    {
        var profiles = store.Profiles.Where(p => profileIds.Contains(p.Id))
            .Select(p => p with { HasPassword = false, HasPassphrase = false, LastUsed = null, UseCount = 0, SessionEnvironment = Array.Empty<EnvironmentEntry>() }).ToList();
        var groupIds = new HashSet<string>();
        foreach (var p in profiles)
            for (var g = store.FindGroup(p.GroupId); g != null && groupIds.Add(g.Id); g = store.FindGroup(g.ParentId)) { }
        var groups = store.Groups.Where(g => groupIds.Contains(g.Id)).ToList();
        return JsonSerializer.SerializeToUtf8Bytes(new ExportFile(Format, 1, groups, profiles), Json);
    }

    public const string AlphaFormat = "Forterm.Avalonia.Profiles/1";
    static readonly HashSet<string> SecretFields = new(StringComparer.OrdinalIgnoreCase)
    { "password", "passphrase", "seed", "seedInput", "totpSecret", "privateKey", "privateKeyData", "encryptedPassword", "encryptedSeed", "encryptedPassphrase", "ticket", "token" };

    /// <summary>Imports a Studio export or an alpha.36 (Forterm.Avalonia.Profiles/1) secret-free export.
    /// Files containing any secret-looking field are refused. Returns how many connections were added.</summary>
    public static int Import(LibraryStore store, string path) => Import(store, path, out _);

    /// <summary>As <see cref="Import(LibraryStore, string)"/>. Commands that would be typed into a
    /// terminal automatically (初始执行, 保活命令), session environment variables and algorithm
    /// preferences are never taken over from a file: someone else's export must not run commands, set
    /// the environment of this user's authenticated shells, or put weak algorithms first.
    /// <paramref name="clearedCommands"/> counts the connections that lost any of these. Exports leave
    /// environment variables out as well.</summary>
    public static int Import(LibraryStore store, string path, out int clearedCommands)
    {
        clearedCommands = 0;
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new InvalidDataException("导入文件超过 4MiB");
        byte[] bytes = File.ReadAllBytes(path);
        using (var doc = Parse(bytes))
        {
            var root = doc.RootElement;
            string format = Text(root, "format");
            if (format == AlphaFormat) return ImportAlpha(store, root);
            if (format != Format) throw new InvalidDataException("不是可识别的 Forterm 连接导出文件（支持 Studio 导出与 alpha.36 无秘密导出）");
        }
        var file = JsonSerializer.Deserialize<ExportFile>(bytes, Json) ?? throw new InvalidDataException("导入文件为空");
        if (file.Format != Format || file.Version != 1) throw new InvalidDataException("不是 Forterm Studio 连接导出文件");
        if (file.Groups == null || file.Profiles == null || file.Groups.Any(g => g == null) || file.Profiles.Any(p => p == null || p.JumpChain == null))
            throw new InvalidDataException("导入文件含缺失或为空的字段");
        if (file.Profiles.Count > 1000 || file.Groups.Count > 1000) throw new InvalidDataException("导入文件超过 1000 项");
        if (file.Groups.Any(g => g.Id == null || g.ParentId == null) || file.Profiles.Any(p => p.Id == null || p.GroupId == null || p.JumpChain.Any(j => j == null)) ||
            file.Groups.Select(g => g.Id).Distinct().Count() != file.Groups.Count || file.Profiles.Select(p => p.Id).Distinct().Count() != file.Profiles.Count)
            throw new InvalidDataException("导入文件含缺失、为空或重复的标识");
        var groupMap = file.Groups.ToDictionary(g => g.Id, _ => StudioProfile.NewId());
        var profileMap = file.Profiles.ToDictionary(p => p.Id, _ => StudioProfile.NewId());
        var groups = file.Groups.Select(g => new ProfileGroup(groupMap[g.Id], g.Name, groupMap.GetValueOrDefault(g.ParentId, ""))).ToList();
        var profiles = file.Profiles.Select(p => p with
        {
            Id = profileMap[p.Id],
            GroupId = groupMap.GetValueOrDefault(p.GroupId, ""),
            JumpChain = p.JumpChain.Where(profileMap.ContainsKey).Select(id => profileMap[id]).ToArray(),
            HasPassword = false,
            HasPassphrase = false,
            Favorite = false,
            LastUsed = null,
            UseCount = 0,
            Created = DateTimeOffset.UtcNow,
            InitialScript = "",
            KeepAliveCommand = "",
            KeepAliveCommandMs = 0,
            SessionEnvironment = Array.Empty<EnvironmentEntry>(),
            Cipher = "",
            KeyExchange = "",
        }).ToList();
        clearedCommands = file.Profiles.Count(p => !string.IsNullOrEmpty(p.InitialScript) || !string.IsNullOrEmpty(p.KeepAliveCommand) || p.SessionEnvironment is { Count: > 0 }
            || !string.IsNullOrEmpty(p.Cipher) || !string.IsNullOrEmpty(p.KeyExchange));
        store.AddRange(groups, profiles);
        return profiles.Count;
    }

    internal static JsonDocument Parse(byte[] bytes, string what = "导入文件")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { throw new InvalidDataException(what + "不是有效的 JSON"); }
        try { Inspect(doc.RootElement, what); return doc; }
        catch { doc.Dispose(); throw; }
    }

    static void Inspect(JsonElement element, string what)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                // Keys chosen by the user (file kinds, shortcut ids) are no field names: ".token" → a language is fine.
                if (property.Name is "editorLanguages" or "shortcuts") continue;
                if (SecretFields.Contains(property.Name)) throw new InvalidDataException(what + "包含秘密字段，已拒绝；请使用无秘密导出");
                Inspect(property.Value, what);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Inspect(child, what);
    }

    static JsonElement Get(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault() : default;

    static string Text(JsonElement root, string name) => Get(root, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";

    static int ImportAlpha(LibraryStore store, JsonElement root)
    {
        var items = Get(root, "profiles");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 1000) throw new InvalidDataException("导入文件需要 profiles 数组，最多 1000 项");
        var raw = items.EnumerateArray().Select(e => new
        {
            Id = Text(e, "id"),
            Name = Text(e, "name"),
            Group = Text(e, "group").Trim(),
            Host = Text(e, "host"),
            Port = Get(e, "port") is { ValueKind: JsonValueKind.Number } p && p.TryGetInt32(out int port) ? port : 22,
            User = Text(e, "user"),
            Protocol = Text(e, "protocol") is var proto && proto is "ssh" or "sftp" or "ftp" or "telnet" ? proto : "ssh",
            Favorite = Get(e, "favorite").ValueKind == JsonValueKind.True,
            AuthMode = Text(e, "authMode") is var mode && mode is "password" or "keyboard" or "key" or "agent" ? mode : "password",
            KeyPath = Text(e, "keyPath"),
            JumpId = Text(e, "jumpId"),
        }).ToList();
        var byId = raw.Where(r => r.Id.Length > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var newIds = byId.Keys.ToDictionary(id => id, _ => StudioProfile.NewId());
        var groups = new List<ProfileGroup>();
        string GroupId(string name)
        {
            if (name.Length == 0) return "";
            if (name.Length > 64) name = name[..64];
            var existing = store.Groups.FirstOrDefault(g => g.ParentId.Length == 0 && g.Name == name) ?? groups.FirstOrDefault(g => g.Name == name);
            if (existing != null) return existing.Id;
            var created = new ProfileGroup(StudioProfile.NewId(), name, "");
            groups.Add(created);
            return created.Id;
        }
        var profiles = new List<StudioProfile>();
        foreach (var r in raw)
        {
            // alpha.36 stores one parent jump per profile; Studio keeps the whole path, first hop first.
            var chain = new List<string>();
            var seen = new HashSet<string> { r.Id };
            for (string next = r.JumpId; next.Length > 0 && chain.Count < LibraryStore.MaxJumps && byId.TryGetValue(next, out var jump) && seen.Add(next); next = jump.JumpId)
                chain.Insert(0, newIds[next]);
            bool ssh = r.Protocol is "ssh" or "sftp";
            var profile = new StudioProfile
            {
                Id = r.Id.Length > 0 && newIds.TryGetValue(r.Id, out var id) ? id : StudioProfile.NewId(),
                Name = r.Name,
                Protocol = r.Protocol,
                Host = r.Host,
                Port = r.Port,
                User = r.User,
                AuthMode = r.Protocol == "ftp" ? "password" : r.AuthMode,
                KeyPath = r.KeyPath,
                GroupId = GroupId(r.Group),
                Favorite = r.Favorite,
                JumpChain = ssh ? chain : Array.Empty<string>(),
            };
            LibraryStore.ValidateProfile(profile);
            profiles.Add(profile);
        }
        store.AddRange(groups, profiles);
        return profiles.Count;
    }
}
