using System.Security.Cryptography;
using System.Text;
using Forterm.Core;

namespace Forterm.Studio.Library;

/// <summary>Saved passwords and key passphrases, only in the OS credential store
/// (Keychain / Credential Manager). There is no plaintext fallback.</summary>
public sealed class CredentialStore(ISecretVault vault)
{
    static string PasswordKey(string id) => "studio/profile/" + id + "/password";
    static string PassphraseKey(string id) => "studio/profile/" + id + "/passphrase";

    public Task<string?> PasswordAsync(string id, CancellationToken ct) => Read(PasswordKey(id), ct);
    public Task<string?> PassphraseAsync(string id, CancellationToken ct) => Read(PassphraseKey(id), ct);
    public Task SavePasswordAsync(string id, string value, CancellationToken ct) => Write(PasswordKey(id), value, ct);
    public Task SavePassphraseAsync(string id, string value, CancellationToken ct) => Write(PassphraseKey(id), value, ct);
    public Task ForgetPasswordAsync(string id, CancellationToken ct) => vault.DeleteAsync(PasswordKey(id), ct);
    public Task ForgetPassphraseAsync(string id, CancellationToken ct) => vault.DeleteAsync(PassphraseKey(id), ct);

    public async Task ForgetAllAsync(string id, CancellationToken ct)
    {
        await vault.DeleteAsync(PasswordKey(id), ct);
        await vault.DeleteAsync(PassphraseKey(id), ct);
    }

    static string AccountKey(string id, string kind) => "studio/account/" + id + "/" + kind;
    public Task<string?> AccountSecretAsync(string id, string kind, CancellationToken ct) => Read(AccountKey(id, kind), ct);
    public Task SaveAccountSecretAsync(string id, string kind, string value, CancellationToken ct) => Write(AccountKey(id, kind), value, ct);
    public Task ForgetAccountSecretAsync(string id, string kind, CancellationToken ct) => vault.DeleteAsync(AccountKey(id, kind), ct);

    async Task<string?> Read(string key, CancellationToken ct)
    {
        var bytes = await vault.ReadAsync(key, ct);
        if (bytes == null) return null;
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    async Task Write(string key, string value, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try { await vault.WriteAsync(key, bytes, ct); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

/// <summary>The OS credential store on Windows/macOS; elsewhere a vault that refuses every
/// operation (saving is reported as unavailable, never written in plaintext).</summary>
internal static class VaultFactory
{
    public static ISecretVault ForCurrentPlatform(string dataDirectory) =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? new OsCredentialVault("studio:" + Path.GetFullPath(dataDirectory)) : new UnavailableVault();
}
