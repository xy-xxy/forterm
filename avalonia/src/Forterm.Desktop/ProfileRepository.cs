using System.Security.Cryptography;
using System.Text;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed record ProfileDraft(ConnectionProfile Profile, string Password, string Passphrase, bool StorePassword, bool StorePassphrase);
public sealed class ProfileRepository(ProfileStore store, ISecretVault vault)
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    readonly SemaphoreSlim gate = gates.GetOrAdd(store.DirectoryPath ?? Guid.NewGuid().ToString(), _ => new(1, 1));
    static string Realm(ConnectionProfile p) => p.Protocol is "ssh" or "sftp" ? "ssh" : p.Protocol;
    public static bool CanReusePassword(ConnectionProfile before, ConnectionProfile after) => Realm(before) == Realm(after) && before.Host.Trim().Equals(after.Host.Trim(), StringComparison.OrdinalIgnoreCase) && before.Port == after.Port && before.User == after.User;
    public static bool CanReusePassphrase(ConnectionProfile before, ConnectionProfile after) => before.KeyPath == after.KeyPath && Realm(before) == Realm(after);
    public static string PasswordKey(ConnectionProfile profile) => Identity(profile) + "/password";
    public static string PassphraseKey(ConnectionProfile profile) => Identity(profile) + "/key-passphrase";
    static string Identity(ConnectionProfile profile) => Guid.TryParse(profile.Id, out var id) ? id.ToString("N") : throw new IOException("连接标识无效，未访问凭据库");
    public async Task<string?> PasswordAsync(ConnectionProfile profile, CancellationToken ct) => await Read(PasswordKey(profile), ct);
    public async Task<string?> PassphraseAsync(ConnectionProfile profile, CancellationToken ct) => await Read(PassphraseKey(profile), ct);
    async Task<string?> Read(string key, CancellationToken ct) { var value = await vault.ReadAsync(key, ct); if (value == null) return null; try { return new UTF8Encoding(false, true).GetString(value); } finally { CryptographicOperations.ZeroMemory(value); } }
    public async Task ImportMetadataAsync(IEnumerable<ConnectionProfile> imported, CancellationToken ct)
    {
        var fresh = MetadataExchange.ReadProfiles(MetadataExchange.ExportProfiles(imported));
        await gate.WaitAsync(ct);
        try { var latest = store.Load(); if (latest.Length + fresh.Length > 10000) throw new IOException("连接库总数超过10000"); ct.ThrowIfCancellationRequested(); store.Save(latest.Concat(fresh)); }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(ConnectionProfile expected, CancellationToken ct)
    {
        await gate.WaitAsync(ct); var backups = new List<(string Key, byte[]? Value)>();
        try
        {
            var latest = store.Load(); if (latest.SingleOrDefault(p => p.Id == expected.Id) != expected) throw new IOException("连接已变化，请重新选择");
            if (latest.Any(p => p.JumpId == expected.Id)) throw new IOException("仍有连接引用此跳板，请先修改这些连接，不会级联删除");
            foreach (string key in new[] { PasswordKey(expected), PassphraseKey(expected) })
            {
                var old = await vault.ReadAsync(key, ct); backups.Add((key, old)); ct.ThrowIfCancellationRequested(); await vault.DeleteAsync(key, ct);
            }
            var final = store.Load(); if (final.SingleOrDefault(p => p.Id == expected.Id) != expected || final.Any(p => p.JumpId == expected.Id)) throw new IOException("删除期间连接或跳板引用已变化，未覆盖");
            ct.ThrowIfCancellationRequested(); store.Save(final.Where(p => p.Id != expected.Id));
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            foreach (var (key, value) in backups.AsEnumerable().Reverse())
            {
                try { if (value == null) await vault.DeleteAsync(key, CancellationToken.None); else await vault.WriteAsync(key, value, CancellationToken.None); } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0) throw new IOException("删除失败且系统凭据回滚未确认，请核对", new AggregateException(new[] { original }.Concat(errors))); throw;
        }
        finally { foreach (var (_, value) in backups) if (value != null) CryptographicOperations.ZeroMemory(value); gate.Release(); }
    }
    public async Task<ConnectionProfile> SaveAsync(ConnectionProfile? previous, ProfileDraft draft, CancellationToken ct)
    {
        await gate.WaitAsync(ct); var backups = new List<(string Key, byte[]? Value)>();
        try
        {
            var latest = store.Load().ToList(); if (latest.GroupBy(p => p.Id).Any(g => g.Count() > 1)) throw new IOException("连接库含重复标识，未更改凭据");
            var current = latest.FirstOrDefault(p => p.Id == draft.Profile.Id);
            if (previous == null && current != null) throw new IOException("连接标识已存在，请重新打开编辑");
            if (previous != null && (current == null || current != previous) && store.DirectoryPath != null) throw new IOException("连接已被其他操作更新或删除，请重新打开编辑");
            if (draft.Profile.Protocol is not ("ssh" or "sftp" or "ftp" or "telnet") || draft.Profile.Protocol is "ftp" or "telnet" && (draft.Profile.AuthMode != "password" || draft.Profile.JumpId.Length > 0)) throw new IOException("协议、认证方式或协议跳板设置无效");
            if (previous != null && previous.HasPassword && draft.StorePassword && (draft.Profile.AuthMode is "password" or "keyboard") && draft.Password.Length == 0 && !CanReusePassword(previous, draft.Profile)) throw new IOException("协议或账号地址已变化，请输入新密码，旧密码不会自动转用");
            if (previous != null && previous.HasPassphrase && draft.StorePassphrase && draft.Profile.AuthMode == "key" && draft.Passphrase.Length == 0 && !CanReusePassphrase(previous, draft.Profile)) throw new IOException("私钥或协议已变化，请重新输入口令");
            async Task<bool> Set(string key, string input, bool save, bool had)
            {
                if (!save && !had) return false; byte[]? old = await vault.ReadAsync(key, ct);
                if (save && input.Length == 0) { try { if (old == null) throw new IOException("没有可保留的系统密码；请输入新值或取消系统保存"); return true; } finally { if (old != null) CryptographicOperations.ZeroMemory(old); } }
                backups.Add((key, old)); ct.ThrowIfCancellationRequested();
                if (save) { byte[] bytes = Encoding.UTF8.GetBytes(input); try { await vault.WriteAsync(key, bytes, ct); } finally { CryptographicOperations.ZeroMemory(bytes); } return true; }
                await vault.DeleteAsync(key, ct); return false;
            }
            bool password = await Set(PasswordKey(draft.Profile), draft.Password, draft.StorePassword && (draft.Profile.AuthMode is "password" or "keyboard"), previous?.HasPassword == true);
            bool phrase = await Set(PassphraseKey(draft.Profile), draft.Passphrase, draft.StorePassphrase && draft.Profile.AuthMode == "key", previous?.HasPassphrase == true);
            ct.ThrowIfCancellationRequested(); var result = draft.Profile with { HasPassword = password, HasPassphrase = phrase, Revision = Guid.NewGuid().ToString("N") };
            var final = store.DirectoryPath == null ? latest : store.Load().ToList(); var finalCurrent = final.FirstOrDefault(p => p.Id == result.Id);
            if (store.DirectoryPath != null && finalCurrent != current) throw new IOException("保存期间连接信息被其他操作更新，未覆盖其修改");
            final.RemoveAll(p => p.Id == result.Id); final.Add(result); await Task.Run(() => store.Save(final), ct); return result;
        }
        catch (Exception original)
        {
            List<Exception> errors = new(); foreach (var (key, value) in backups.AsEnumerable().Reverse())
            {
                try { if (value == null) await vault.DeleteAsync(key, CancellationToken.None); else await vault.WriteAsync(key, value, CancellationToken.None); } catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 0) throw new IOException("保存失败且系统凭据回滚未确认；连接信息未保存，请核对保管库。当前输入保留。", new AggregateException(new[] { original }.Concat(errors))); throw;
        }
        finally { foreach (var (_, value) in backups) if (value != null) CryptographicOperations.ZeroMemory(value); gate.Release(); }
    }
}
