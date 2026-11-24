using System.Text;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class AccountSafetyTests
{
    [Fact] public async Task ExportCancelOrTargetChangePreservesExistingFileAndNewExportIsPrivate()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string path = Path.Combine(dir, "config.json");
        try
        {
            File.WriteAllText(path, "old"); Assert.False(await LocalTextFiles.WriteAsync(path, "new", (_, _) => Task.FromResult(false), CancellationToken.None)); Assert.Equal("old", File.ReadAllText(path));
            await Assert.ThrowsAsync<IOException>(() => LocalTextFiles.WriteAsync(path, "new", (_, _) => { File.WriteAllText(path, "external edit"); return Task.FromResult(true); }, CancellationToken.None)); Assert.Equal("external edit", File.ReadAllText(path));
            string fresh = Path.Combine(dir, "new.json"); Assert.True(await LocalTextFiles.WriteAsync(fresh, "{}", (_, _) => throw new Exception("new target must not ask"), CancellationToken.None));
            Assert.Equal("{}", File.ReadAllText(fresh)); if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fresh)); Assert.Empty(Directory.GetFiles(dir, ".forterm-config-*"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact] public async Task AccountMetadataRaceRestoresSecretAndPreservesUnrelatedAccounts()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-account-race-" + Guid.NewGuid().ToString("N")); var vault = new CredentialStoreTests.MemoryVault(); var repository = new AccountRepository(dir, vault);
        try
        {
            var first = await repository.SaveAsync(null, new(new() { Name = "first", PortalUrl = "https://example.invalid", Username = "user" }, "old", ""), CancellationToken.None);
            string path = Path.Combine(dir, "bastion-accounts.json"); var unrelated = new BastionAccount { Name = "external", PortalUrl = "https://example.invalid", Username = "other" };
            vault.AfterWrite = () => File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { first, unrelated }));
            first = await repository.SaveAsync(first, new(first with { Name = "renamed" }, "new", ""), CancellationToken.None); Assert.Equal(2, repository.Load().Length); Assert.Equal(unrelated, repository.Load().Single(a => a.Id == unrelated.Id));
            vault.AfterWrite = () => File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { first with { Name = "external rename" }, unrelated }));
            await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(first, new(first, "must roll back", ""), CancellationToken.None)); Assert.Equal("new", Encoding.UTF8.GetString(vault.Values[AccountRepository.SecretKey(first, "password")])); Assert.Equal("external rename", repository.Load().Single(a => a.Id == first.Id).Name);
            int before = vault.Reads + vault.Deletes; await Assert.ThrowsAsync<IOException>(() => repository.DeleteAsync(first, CancellationToken.None)); Assert.Equal(before, vault.Reads + vault.Deletes);
            await repository.DeleteAsync(repository.Load().Single(a => a.Id == first.Id), CancellationToken.None); Assert.Single(repository.Load()); Assert.Empty(vault.Values);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
