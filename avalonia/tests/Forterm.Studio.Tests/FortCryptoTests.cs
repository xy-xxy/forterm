using Forterm.Studio.Services.Bastion;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>What the fort portal's scripts do to a request, rebuilt (see <see cref="FortCrypto"/>).</summary>
public sealed class FortCryptoTests
{
    [Fact]
    public void Sm3MatchesTheStandard()
    {
        // GB/T 32905-2016, appendix A.
        Assert.Equal("66c7f0f462eeedd9d1f2d46bdc10e4e24167c4875cf2f7a2297da02b8f4ba8e0", FortCrypto.Sm3Hex("abc"));
        Assert.Equal("debe9ff92275b8a138604889c18e5a4d6fdb70e5387e5765293dcba39c0c5732", FortCrypto.Sm3Hex(string.Concat(Enumerable.Repeat("abcd", 16))));
        Assert.Equal(64, FortCrypto.Sm3Hex("").Length);
        Assert.Equal("1ab21d8355cfa17f8e61194831e81a8f22bec8c728fefb747ed035eb5082aa2b", FortCrypto.Sm3Hex(""));
    }

    [Fact]
    public void Encode64IsTheScriptsOwn()
    {
        // base64("123456") = "MTIzNDU2" → every third followed by the one before → "MTIzINDUD2" → reversed.
        Assert.Equal("2DUDNIzITM", FortCrypto.Encode64("123456"));
        Assert.Equal(10, FortCrypto.Encode64("654321").Length);
        // A "+" of base64 is written as %2B: ">>>" is "Pj4+".
        Assert.Contains("%2B", FortCrypto.Encode64(">>>"));
    }

    /// <summary>What the portal's own <c>encrypt()</c> (myajax.js, run in node) gives for these digits.</summary>
    [Theory]
    [InlineData("00000000", "a8b0a7b1a9b2a2b2a7b0a8b1a2b7a9b7")]
    [InlineData("12345678", "o8p0c7d8i9h7k2l7g7j0m8n1q2r7e9f2")]
    [InlineData("99999999", "s8t9s7t8s9t7s2t7s7t9s8t8s2t2s9t2")]
    [InlineData("95748316", "m8n9i7j1o9r2k2l2q7p9g8h8e2f7s9t2")]
    [InlineData("31415926", "m8n9e7f1i9l7e2f2k7j0s8t8c2d2g9h7")]
    [InlineData("27182818", "o8p9q7r8c9f2o2p2e7d9o8p8e2f7c9d2")]
    [InlineData("64230400", "a8b0k7l1a9d7g2h7c7b0k8l8a2b7m9n2")]
    [InlineData("80808080", "a8b9a7b1o9p7a2b2o7p9a8b1o2p2o9p7")]
    public void TheSignatureIsThePortalsOwn(string digits, string signed) => Assert.Equal(signed, FortCrypto.Encrypt(digits));

    [Fact]
    public void TheSignatureGoesBothWays()
    {
        for (int i = 0; i < 500; i++)
        {
            string digits = Random.Shared.Next(0, 100_000_000).ToString("D8");
            string signed = FortCrypto.Encrypt(digits);
            Assert.Equal(32, signed.Length);
            Assert.Equal(digits, FortCrypto.Decrypt(signed));
        }
        Assert.Equal(32, FortCrypto.Rd2().Length);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_795_748_316_576);
        Assert.Equal("95748316", FortCrypto.Decrypt(FortCrypto.Rd1(now)));
        Assert.Throws<ArgumentException>(() => FortCrypto.Encrypt("1234567"));
    }
}
