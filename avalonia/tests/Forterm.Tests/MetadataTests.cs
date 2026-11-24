using System.Text.Json;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class MetadataTests
{
    [Fact] public async Task ProfileRoundtripRemapsJumpsAndAppendCannotTouchVaultOrExistingCredentials()
    {
        var jump = new ConnectionProfile("jump", "group", "example.invalid", 22, "user") { HasPassword = true };
        var child = new ConnectionProfile("child", "group", "internal.invalid", 22, "user") { JumpId = jump.Id, HasPassphrase = true, AuthMode = "key", KeyPath = "/not-opened/key" };
        string json = MetadataExchange.ExportProfiles([jump, child]); var parsed = MetadataExchange.ReadProfiles(json);
        Assert.NotEqual(jump.Id, parsed[0].Id); Assert.Equal(parsed[0].Id, parsed[1].JumpId); Assert.All(parsed, p => { Assert.False(p.HasPassword); Assert.False(p.HasPassphrase); });
        string dir = Path.Combine(Path.GetTempPath(), "forterm-import-" + Guid.NewGuid().ToString("N")); var vault = new CredentialStoreTests.MemoryVault(); var store = new ProfileStore(dir); var repo = new ProfileRepository(store, vault);
        try
        {
            var existing = await repo.SaveAsync(null, new(jump, "saved-only", "", true, false), CancellationToken.None); int before = vault.Reads + vault.Writes + vault.Deletes;
            await repo.ImportMetadataAsync(parsed, CancellationToken.None); Assert.Equal(before, vault.Reads + vault.Writes + vault.Deletes); Assert.Equal(existing, store.Load().Single(p => p.Id == existing.Id)); Assert.Equal(3, store.Load().Length);
            Assert.DoesNotContain("saved-only", MetadataExchange.ExportProfiles(store.Load()));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact] public async Task LegacyGoMetadataIsExplicitAndAccountsImportNeverReusesSecretIdentity()
    {
        string legacy = """{"version":1,"profiles":[{"id":"old-id","title":"legacy","group":"g","protocol":"sftp","host":"example.invalid","port":22,"username":"user","auth":"","hasPassword":true}],"bastionAccounts":[{"id":"old-account","name":"public","portalUrl":"https://example.invalid","username":"user","totp":{"algorithm":"SHA512","digits":8,"period":60},"hasTotpSecret":true,"totpEnabled":true}],"knownHosts":{"ignored":"SHA256:untrusted"}}""";
        var profile = Assert.Single(MetadataExchange.ReadProfiles(legacy)); Assert.True(Guid.TryParse(profile.Id, out _)); Assert.Equal("legacy", profile.Name); Assert.False(profile.HasPassword);
        var account = Assert.Single(MetadataExchange.ReadAccounts(legacy)); Assert.True(Guid.TryParse(account.Id, out _)); Assert.False(account.HasSeed); Assert.False(account.TotpEnabled); Assert.Equal("SHA512", account.Totp.Algorithm);
        var vault = new CredentialStoreTests.MemoryVault(); var repository = new AccountRepository(null, vault); await repository.ImportMetadataAsync([account with { HasPassword = true, HasSeed = true, TotpEnabled = true }], CancellationToken.None);
        var saved = Assert.Single(repository.Load()); Assert.NotEqual(account.Id, saved.Id); Assert.False(saved.HasPassword); Assert.False(saved.HasSeed); Assert.False(saved.TotpEnabled); Assert.Equal(0, vault.Reads + vault.Writes + vault.Deletes);
    }
    [Theory]
    [InlineData("{\"Format\":\"Forterm.Avalonia.Profiles/1\",\"Profiles\":[],\"password\":\"\"}")]
    [InlineData("{\"Format\":\"Forterm.Avalonia.Profiles/1\",\"Profiles\":[],\"extra\":{\"seed\":\"public\"}}")]
    [InlineData("{\"Format\":\"Forterm.Avalonia.Profiles/1\",\"Profiles\":[],\"profiles\":[]}")]
    [InlineData("{\"Format\":\"wrong\",\"Profiles\":[]}")]
    public void SecretFieldsDuplicatePropertiesAndWrongFormatAreRefused(string json) => Assert.Throws<IOException>(() => MetadataExchange.ReadProfiles(json));
    [Fact] public void DuplicateMissingOrCyclicJumpsInvalidEndpointsAndOversizeAreRefused()
    {
        var a = new ConnectionProfile("one", "g", "example.invalid", 22, "user"); var b = a with { Name = "two", Id = Guid.NewGuid().ToString("N") };
        foreach (var profiles in new[] { new[] { a, a }, new[] { a with { JumpId = "missing" } }, new[] { a with { JumpId = b.Id }, b with { JumpId = a.Id } }, new[] { a with { Host = "ssh://bad" } }, new[] { a with { Port = 0 } }, new[] { a with { User = null! } } }) Assert.Throws<IOException>(() => MetadataExchange.ReadProfiles(MetadataExchange.ExportProfiles(profiles)));
        Assert.Throws<IOException>(() => MetadataExchange.ReadProfiles(new string(' ', 4 * 1024 * 1024 + 1)));
    }
}
