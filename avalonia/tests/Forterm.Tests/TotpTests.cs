using System.Security.Cryptography;
using System.Text;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class TotpTests
{
    public const string PublicSeed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    [Theory]
    [InlineData(59L, "94287082", "46119246", "90693936")]
    [InlineData(1111111109L, "07081804", "68084774", "25091201")]
    [InlineData(1111111111L, "14050471", "67062674", "99943326")]
    [InlineData(1234567890L, "89005924", "91819424", "93441116")]
    [InlineData(2000000000L, "69279037", "90698825", "38618901")]
    [InlineData(20000000000L, "65353130", "77737706", "47863826")]
    public void Rfc6238VectorsForAllAlgorithmsIncludingBeyond2038(long at, string sha1, string sha256, string sha512)
    {
        foreach (var (algorithm, length, expected) in new[] { ("SHA1", 20, sha1), ("SHA256", 32, sha256), ("SHA512", 64, sha512) })
        {
            byte[] key = Enumerable.Range(0, length).Select(i => (byte)('0' + (i + 1) % 10)).ToArray();
            var code = Totp.Generate(key, new(algorithm, 8, 30), DateTimeOffset.FromUnixTimeSeconds(at)); Assert.Equal(expected, code.Code); Assert.InRange(code.RemainingSeconds, 1, 30);
        }
    }
    [Fact] public void CanonicalBase32AndUriStandaloneDefaultsAreStrict()
    {
        var value = Totp.Normalize("otpauth://totp/public?secret=" + PublicSeed, new("SHA512", 8, 60));
        Assert.Equal(new TotpParameters(), value.Parameters); Assert.Equal(PublicSeed, Totp.EncodeBase32(value.Secret)); Assert.Equal("287082", Totp.Generate(value.Secret, value.Parameters, DateTimeOffset.FromUnixTimeSeconds(59)).Code);
        Assert.Equal(Encoding.ASCII.GetBytes("12345678901234567890"), Totp.DecodeBase32("gez-dgnbv gy3tqojqgez dgnbvgy3tqojq"));
        CryptographicOperations.ZeroMemory(value.Secret);
    }
    [Theory] [InlineData("MY======")] [InlineData("MZ======")] [InlineData("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ=")] [InlineData("A1ZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData("otpauth://hotp/name?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData("otpauth://totp/name?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&digits=7")]
    [InlineData("otpauth://totp/name?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&period=301")]
    [InlineData("otpauth://totp/name?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&secret=A")]
    [InlineData("otpauth://user@totp/name?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData("otpauth://totp/%ZZ?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    public void InvalidSeedOrUriCannotSilentlyDowngrade(string input) => Assert.Throws<IOException>(() => Totp.Normalize(input));
    [Fact] public async Task AccountVaultCanonicalRoundtripExportAndFailurePreserveOldValues()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-totp-" + Guid.NewGuid().ToString("N")); var vault = new CredentialStoreTests.MemoryVault(); var repository = new AccountRepository(dir, vault);
        try
        {
            var draft = new BastionAccount { Name = "synthetic", Username = "user", PortalUrl = "https://example.invalid", TotpEnabled = true, Totp = new("SHA512", 8, 60) };
            var saved = await repository.SaveAsync(null, new(draft, "synthetic-password", "otpauth://totp/public?secret=" + PublicSeed), CancellationToken.None);
            Assert.Equal(new TotpParameters(), saved.Totp); Assert.Equal(PublicSeed, Encoding.ASCII.GetString(vault.Values[AccountRepository.SecretKey(saved, "totp")]));
            var read = await repository.ReadTotpAsync(saved, CancellationToken.None); try { Assert.Equal("287082", Totp.Generate(read.Configuration.Secret, read.Configuration.Parameters, DateTimeOffset.FromUnixTimeSeconds(59)).Code); } finally { CryptographicOperations.ZeroMemory(read.Configuration.Secret); }
            string metadata = File.ReadAllText(Path.Combine(dir, "bastion-accounts.json")), export = repository.ExportMetadata(); Assert.DoesNotContain(PublicSeed, metadata + export); Assert.DoesNotContain("synthetic-password", metadata + export); Assert.Contains("\"HasSeed\": false", export);
            int before = vault.Reads + vault.Writes + vault.Deletes; await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(saved, new(saved with { PortalUrl = "https://other.invalid" }, "", ""), CancellationToken.None)); Assert.Equal(before, vault.Reads + vault.Writes + vault.Deletes);
            vault.FailWrite = true; await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(saved, new(saved, "new-password", ""), CancellationToken.None)); vault.FailWrite = false;
            Assert.Equal(metadata, File.ReadAllText(Path.Combine(dir, "bastion-accounts.json"))); Assert.Equal(PublicSeed, Encoding.ASCII.GetString(vault.Values[AccountRepository.SecretKey(saved, "totp")]));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
