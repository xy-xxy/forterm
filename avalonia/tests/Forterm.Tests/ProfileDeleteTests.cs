using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class ProfileDeleteTests
{
    sealed class FaultVault : ISecretVault
    {
        public readonly CredentialStoreTests.MemoryVault Inner = new(); public bool FailSecondDelete; int deletes;
        public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Inner.ReadAsync(key, ct);
        public Task WriteAsync(string key, byte[] value, CancellationToken ct) => Inner.WriteAsync(key, value, ct);
        public Task DeleteAsync(string key, CancellationToken ct) { if (FailSecondDelete && ++deletes == 2) throw new IOException("synthetic delete failure"); return Inner.DeleteAsync(key, ct); }
    }
    [Fact] public async Task DeletionRefusesReferencedJumpBeforeVaultAndPartialVaultFailureRestoresMetadataAndSecrets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-delete-" + Guid.NewGuid().ToString("N")); var store = new ProfileStore(dir); var vault = new FaultVault(); var repo = new ProfileRepository(store, vault);
        try
        {
            var profile = new ConnectionProfile("jump", "g", "example.invalid", 22, "user") { HasPassword = true, HasPassphrase = true }; var child = profile with { Id = Guid.NewGuid().ToString("N"), Name = "child", JumpId = profile.Id }; store.Save([profile, child]);
            await Assert.ThrowsAsync<IOException>(() => repo.DeleteAsync(profile, CancellationToken.None)); Assert.Equal(0, vault.Inner.Reads + vault.Inner.Deletes);
            store.Save([profile]); vault.Inner.Values[ProfileRepository.PasswordKey(profile)] = [1, 2, 3]; vault.Inner.Values[ProfileRepository.PassphraseKey(profile)] = [4, 5, 6]; vault.FailSecondDelete = true;
            await Assert.ThrowsAsync<IOException>(() => repo.DeleteAsync(profile, CancellationToken.None)); Assert.Equal(profile, Assert.Single(store.Load())); Assert.Equal(new byte[] { 1, 2, 3 }, vault.Inner.Values[ProfileRepository.PasswordKey(profile)]); Assert.Equal(new byte[] { 4, 5, 6 }, vault.Inner.Values[ProfileRepository.PassphraseKey(profile)]);
            vault.FailSecondDelete = false; await repo.DeleteAsync(profile, CancellationToken.None); Assert.Empty(store.Load()); Assert.Empty(vault.Inner.Values);
        }
        finally { Directory.Delete(dir, true); }
    }
}
