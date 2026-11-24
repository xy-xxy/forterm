using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed record BastionAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N"); public string Revision { get; init; } = "0";
    public string Name { get; init; } = ""; public string PortalUrl { get; init; } = ""; public string Username { get; init; } = "";
    public TotpParameters Totp { get; init; } = new(); public bool TotpEnabled { get; init; } public bool HasPassword { get; init; } public bool HasSeed { get; init; }
    public override string ToString() => Name + " · " + Username + " · " + PortalUrl;
}
public sealed record AccountDraft(BastionAccount Account, string Password, string Seed, bool ClearPassword = false, bool ClearSeed = false);
public sealed class AccountRepository(string? directory, ISecretVault vault)
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    readonly SemaphoreSlim gate = gates.GetOrAdd(directory ?? Guid.NewGuid().ToString(), _ => new(1, 1)); BastionAccount[] memory = [];
    public static string SecretKey(BastionAccount account, string kind)
    {
        if (!Guid.TryParse(account.Id, out var id) || kind is not ("password" or "totp")) throw new IOException("账户标识或秘密类别无效");
        return "bastion/" + id.ToString("N") + "/" + kind;
    }
    public static void Validate(BastionAccount account)
    {
        if (account == null || account.Name == null || account.Username == null || account.PortalUrl == null || account.Totp == null || !Guid.TryParse(account.Id, out _) || account.Name.Length is < 1 or > 256 || account.Username.Length is < 1 or > 1024 || account.PortalUrl.Length > 4096 ||
            !Uri.TryCreate(account.PortalUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new IOException("账户名称、入口或用户无效；入口不能含凭据、查询或片段");
        account.Totp.Validate();
    }
    public BastionAccount[] Load()
    {
        if (directory == null) return memory.ToArray(); string path = Path.Combine(directory, "bastion-accounts.json"); if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("账户库超过4MiB边界");
        var accounts = JsonSerializer.Deserialize<BastionAccount[]>(File.ReadAllText(path)) ?? [];
        foreach (var account in accounts) Validate(account);
        if (accounts.Length > 10000 || accounts.GroupBy(a => a.Id).Any(g => g.Count() > 1)) throw new IOException("账户数量或标识重复");
        return accounts;
    }
    void SaveMetadata(BastionAccount[] accounts)
    {
        if (directory == null) { memory = accounts.ToArray(); return; }
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "bastion-accounts.json"), temp = Path.Combine(directory, "accounts-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }); if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new IOException("账户库保存超过4MiB");
            File.WriteAllText(temp, json); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static bool SameRealm(BastionAccount a, BastionAccount b) => a.Username == b.Username && new Uri(a.PortalUrl).AbsoluteUri == new Uri(b.PortalUrl).AbsoluteUri;
    public async Task<BastionAccount> SaveAsync(BastionAccount? previous, AccountDraft draft, CancellationToken ct)
    {
        Validate(draft.Account);
        if (draft.Password.Length > 32768 || draft.Seed.Length > 4096) throw new IOException("账户秘密输入超出边界");
        await gate.WaitAsync(ct); var backups = new List<(string Key, byte[]? Value)>(); byte[]? canonicalSeed = null; TotpParameters parameters = draft.Account.Totp;
        try
        {
            var latest = Load(); var current = latest.SingleOrDefault(a => a.Id == draft.Account.Id);
            if (previous == null ? current != null : current != previous) throw new IOException("账户已被其他操作更新，请重新打开");
            if (previous != null && !SameRealm(previous, draft.Account) && (previous.HasPassword && !draft.ClearPassword && draft.Password.Length == 0 || previous.HasSeed && !draft.ClearSeed && draft.Seed.Length == 0)) throw new IOException("入口或用户名变化，请明确输入新秘密或选择清除；不自动转用旧账户秘密");
            if (draft.Seed.Length > 0 && !draft.ClearSeed)
            {
                var normalized = Totp.Normalize(draft.Seed, parameters); try { canonicalSeed = Encoding.ASCII.GetBytes(Totp.EncodeBase32(normalized.Secret)); parameters = normalized.Parameters; } finally { CryptographicOperations.ZeroMemory(normalized.Secret); }
            }
            async Task<bool> Update(string kind, byte[]? replacement, bool clear, bool had)
            {
                string key = SecretKey(draft.Account, kind); if (replacement == null && !clear) return had;
                byte[]? old = await vault.ReadAsync(key, ct); backups.Add((key, old)); ct.ThrowIfCancellationRequested();
                if (clear) { await vault.DeleteAsync(key, ct); return false; }
                await vault.WriteAsync(key, replacement!, ct); return true;
            }
            byte[]? password = draft.Password.Length > 0 ? Encoding.UTF8.GetBytes(draft.Password) : null; bool hasPassword;
            try { hasPassword = await Update("password", password, draft.ClearPassword, previous?.HasPassword == true); } finally { if (password != null) CryptographicOperations.ZeroMemory(password); }
            bool hasSeed = await Update("totp", canonicalSeed, draft.ClearSeed, previous?.HasSeed == true);
            if (draft.Account.TotpEnabled && !hasSeed) throw new IOException("启用验证器需要已保存的key");
            var result = draft.Account with { Totp = parameters, HasPassword = hasPassword, HasSeed = hasSeed, Revision = Guid.NewGuid().ToString("N") };
            var final = Load(); if (final.SingleOrDefault(a => a.Id == result.Id) != current) throw new IOException("保存过程中账户元数据已变化，未覆盖");
            await Task.Run(() => SaveMetadata(final.Where(a => a.Id != result.Id).Append(result).ToArray()), ct); return result;
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            foreach (var backup in backups.AsEnumerable().Reverse()) { try { if (backup.Value == null) await vault.DeleteAsync(backup.Key, CancellationToken.None); else await vault.WriteAsync(backup.Key, backup.Value, CancellationToken.None); } catch (Exception error) { errors.Add(error); } }
            if (errors.Count > 0) throw new IOException("账户保存失败且系统凭据回滚未确认，输入保留，请核对", new AggregateException(new[] { original }.Concat(errors))); throw;
        }
        finally { if (canonicalSeed != null) CryptographicOperations.ZeroMemory(canonicalSeed); foreach (var b in backups) if (b.Value != null) CryptographicOperations.ZeroMemory(b.Value); gate.Release(); }
    }
    public async Task<(BastionAccount Account, TotpConfiguration Configuration)> ReadTotpAsync(BastionAccount expected, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = Load().SingleOrDefault(a => a.Id == expected.Id); if (current != expected || !expected.HasSeed || !expected.TotpEnabled) throw new IOException("账户已变化或验证器未启用，请重新打开");
            var raw = await vault.ReadAsync(SecretKey(expected, "totp"), ct) ?? throw new IOException("系统验证器key不可用，无明文回退");
            try { return (current, Totp.Normalize(new UTF8Encoding(false, true).GetString(raw), current.Totp)); } finally { CryptographicOperations.ZeroMemory(raw); }
        }
        finally { gate.Release(); }
    }
    public string ExportMetadata() => JsonSerializer.Serialize(Load().Select(a => a with { HasPassword = false, HasSeed = false, TotpEnabled = false, Revision = "0" }), new JsonSerializerOptions { WriteIndented = true });
    public async Task ImportMetadataAsync(IEnumerable<BastionAccount> imported, CancellationToken ct)
    {
        var fresh = MetadataExchange.ReadAccounts(MetadataExchange.ExportAccounts(imported));
        await gate.WaitAsync(ct);
        try { var latest = Load(); if (latest.Length + fresh.Length > 10000) throw new IOException("账户库总数超过10000"); ct.ThrowIfCancellationRequested(); SaveMetadata(latest.Concat(fresh).ToArray()); }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(BastionAccount expected, CancellationToken ct)
    {
        await gate.WaitAsync(ct); var backups = new List<(string Key, byte[]? Value)>();
        try
        {
            var latest = Load(); if (latest.SingleOrDefault(a => a.Id == expected.Id) != expected) throw new IOException("账户已变化，未删除");
            foreach (string kind in new[] { "password", "totp" }) { string key = SecretKey(expected, kind); var old = await vault.ReadAsync(key, ct); backups.Add((key, old)); await vault.DeleteAsync(key, ct); }
            var final = Load(); if (final.SingleOrDefault(a => a.Id == expected.Id) != expected) throw new IOException("删除过程中账户已变化，未覆盖");
            await Task.Run(() => SaveMetadata(final.Where(a => a.Id != expected.Id).ToArray()), ct);
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            foreach (var backup in backups.AsEnumerable().Reverse())
            {
                try { if (backup.Value == null) await vault.DeleteAsync(backup.Key, CancellationToken.None); else await vault.WriteAsync(backup.Key, backup.Value, CancellationToken.None); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0) throw new IOException("账户删除失败且系统凭据回滚未确认，请核对", new AggregateException(new[] { original }.Concat(errors)));
            throw;
        }
        finally { foreach (var b in backups) if (b.Value != null) CryptographicOperations.ZeroMemory(b.Value); gate.Release(); }
    }
}
