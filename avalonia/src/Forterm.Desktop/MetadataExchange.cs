using System.Text;
using System.Text.Json;
namespace Forterm.Desktop;

/// <summary>Explicit metadata exchange. Never opens a vault, key file or network endpoint.</summary>
public static class MetadataExchange
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static readonly HashSet<string> SecretFields = new(StringComparer.OrdinalIgnoreCase) { "password", "passphrase", "seed", "seedInput", "totpSecret", "privateKey", "privateKeyData", "encryptedPassword", "encryptedSeed", "encryptedPassphrase", "ticket", "token" };
    public static string ExportProfiles(IEnumerable<ConnectionProfile> profiles) => JsonSerializer.Serialize(new { Format = "Forterm.Avalonia.Profiles/1", Profiles = profiles.Select(p => p with { HasPassword = false, HasPassphrase = false, Revision = "0" }) }, Json);
    public static string ExportAccounts(IEnumerable<BastionAccount> accounts) => JsonSerializer.Serialize(new { Format = "Forterm.Avalonia.Accounts/1", Accounts = accounts.Select(a => a with { HasPassword = false, HasSeed = false, TotpEnabled = false, Revision = "0" }) }, Json);
    static JsonDocument Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new IOException("导入文件超过4MiB");
        JsonDocument doc; try { doc = JsonDocument.Parse(json, new() { MaxDepth = 32 }); } catch (JsonException) { throw new IOException("导入JSON无效"); }
        try { Inspect(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
    }
    static void Inspect(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new IOException("导入含重复字段");
                if (SecretFields.Contains(property.Name)) throw new IOException("导入含秘密字段，请先使用无秘密导出"); Inspect(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Inspect(child);
    }
    static JsonElement Get(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault() : default;
    static string Text(JsonElement root, string name, string fallback = "") { var value = Get(root, name); if (value.ValueKind == JsonValueKind.Undefined) return fallback; if (value.ValueKind != JsonValueKind.String) throw new IOException("导入文本字段无效"); return value.GetString()!; }
    static bool Legacy(JsonElement root) => Get(root, "version") is var v && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int number) && number == 1;
    static JsonElement Items(JsonElement root, string field, string format, bool legacy, bool bareArray = false)
    {
        if (bareArray && root.ValueKind == JsonValueKind.Array) return root;
        if (!legacy && Text(root, "format") != format) throw new IOException("请选择相应Forterm无秘密配置导出文件");
        var items = Get(root, field); if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 1000) throw new IOException("导入需要数组且最多1000项"); return items;
    }
    static string Id(string value) => Guid.TryParse(value, out var id) ? id.ToString("N") : value;
    public static ConnectionProfile[] ReadProfiles(string json)
    {
        using var doc = Parse(json); bool legacy = Legacy(doc.RootElement); var rows = Items(doc.RootElement, "profiles", "Forterm.Avalonia.Profiles/1", legacy); var profiles = new List<ConnectionProfile>();
        try
        {
            foreach (var item in rows.EnumerateArray())
            {
                ConnectionProfile p;
                if (legacy)
                {
                    var port = Get(item, "port"); if (!port.TryGetInt32(out int number)) throw new IOException("导入端口无效");
                    p = new(Text(item, "title"), Text(item, "group"), Text(item, "host"), number, Text(item, "username")) { Id = Text(item, "id"), Protocol = Text(item, "protocol", "ssh"), AuthMode = Text(item, "auth", "password"), KeyPath = Text(item, "keyPath"), JumpId = Text(item, "jumpId") };
                }
                else p = item.Deserialize<ConnectionProfile>(Json) ?? throw new IOException("连接项无效");
                if (legacy && p.AuthMode == "") p = p with { AuthMode = "password" };
                p = p with { Id = Id(p.Id), JumpId = Id(p.JumpId), HasPassword = false, HasPassphrase = false, Revision = "0" }; ValidateProfile(p); profiles.Add(p);
            }
            var ids = profiles.ToDictionary(p => p.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
            foreach (var p in profiles) ConnectionCoordinator.ResolveChain(p, profiles);
            return profiles.Select(p => p with { Id = ids[p.Id], JumpId = p.JumpId.Length == 0 ? "" : ids[p.JumpId] }).ToArray();
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException or KeyNotFoundException) { throw new IOException("导入连接无效、重复或缺少跳板", e); }
    }
    public static void ValidateProfile(ConnectionProfile p)
    {
        string?[] fields = [p.Id, p.Name, p.Group, p.Host, p.User, p.Protocol, p.AuthMode, p.KeyPath, p.AgentFingerprint, p.JumpId];
        if (fields.Any(s => s == null || s.Any(char.IsControl)) || p.Id.Length is < 1 or > 128 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 256 || p.Group.Length > 256 || string.IsNullOrWhiteSpace(p.Host) || p.Host.Length > 1024 || p.Host.Any(char.IsWhiteSpace) || p.Host.IndexOfAny(['/', '\\', '@']) >= 0 || p.Port is < 1 or > 65535 || p.User.Length is < 1 or > 1024 || p.KeyPath.Length > 4096 || p.AgentFingerprint.Length > 256 || p.JumpId.Length > 128 || p.Protocol is not ("ssh" or "sftp" or "ftp" or "telnet") || p.AuthMode is not ("password" or "keyboard" or "key" or "agent") || p.AuthMode == "key" && p.KeyPath.Length == 0 || p.Protocol is "ftp" or "telnet" && (p.AuthMode != "password" || p.JumpId.Length > 0 || p.KeyPath.Length > 0)) throw new IOException("导入连接名称、地址、端口、认证或跳板无效");
    }
    public static BastionAccount[] ReadAccounts(string json)
    {
        using var doc = Parse(json); bool legacy = Legacy(doc.RootElement); var rows = Items(doc.RootElement, legacy ? "bastionAccounts" : "accounts", "Forterm.Avalonia.Accounts/1", legacy, true);
        if (rows.GetArrayLength() > 1000) throw new IOException("账户导入最多1000项"); var result = new List<BastionAccount>();
        try
        {
            foreach (var item in rows.EnumerateArray())
            {
                var account = item.Deserialize<BastionAccount>(Json) ?? throw new IOException("账户项无效");
                account = account with { Id = Guid.NewGuid().ToString("N"), Revision = "0", HasPassword = false, HasSeed = false, TotpEnabled = false }; AccountRepository.Validate(account); result.Add(account);
            }
        }
        catch (JsonException e) { throw new IOException("账户项格式无效", e); }
        return result.ToArray();
    }
    public static async Task<string> ReadFileAsync(string path, CancellationToken ct)
    {
        path = Forterm.Core.LocalStamp.FullPath(path); var parent = Forterm.Core.LocalStamp.RequireParent(path); var before = Forterm.Core.LocalStamp.Read(path); if (before == null || before.Directory || before.Length > 4 * 1024 * 1024) throw new IOException("导入需要4MiB以内普通文件");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, FileOptions.Asynchronous); using var memory = new MemoryStream(); byte[] buffer = new byte[32768]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0) { if (memory.Length + count > 4 * 1024 * 1024) throw new IOException("导入超过4MiB"); await memory.WriteAsync(buffer.AsMemory(0, count), ct); }
        var after = Forterm.Core.LocalStamp.Read(path); if (before != after || memory.Length != before.Length || !Forterm.Core.LocalStamp.SameDirectory(parent, Forterm.Core.LocalStamp.RequireParent(path))) throw new IOException("导入文件已变化，请重选"); return new UTF8Encoding(false, true).GetString(memory.ToArray()).TrimStart('\uFEFF');
    }
}
