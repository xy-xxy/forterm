using Forterm.Core;
using Forterm.Studio.Services.Bastion;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>The fort portal client against a stand-in portal (tooling/fort_portal_fixture.py, 127.0.0.1 only) that checks
/// every call as the recorded portal's pages make it: signatures, SM3, the app code, cookies (changed at login, as recorded)
/// and tokens.</summary>
public sealed class FortPortalTests
{
    const string Tenant = "synthetic-tenant", User = "synthetic-user", Password = "synthetic-password";
    static readonly byte[] Seed = Totp.DecodeBase32("JBSWY3DPEHPK3PXP");
    static string Now() => Totp.Generate(Seed, new TotpParameters(), DateTimeOffset.UtcNow).Code;
    static Task<string?> Code(CancellationToken _) => Task.FromResult<string?>(Now());
    static Task<FortCredentials?> NoQuestion(FortAccount a, bool fixedName, CancellationToken _) => throw new Xunit.Sdk.XunitException("nothing should be asked for " + a.Name);
    static Func<FortAccount, bool, CancellationToken, Task<FortCredentials?>> Answer(string user, string password) => (_, _, _) => Task.FromResult<FortCredentials?>(new FortCredentials(user, password));

    static FortPortal Portal(LoopbackFixture fort, string? pin = "") => new(new Uri($"https://127.0.0.1:{fort.Port}/fort/login"), pin == "" ? fort.Fingerprint : pin);

    static List<string> Calls(LoopbackFixture fort) => fort.Stats().GetProperty("calls").EnumerateArray().Select(c => c.GetString()!).ToList();

    [Fact]
    public async Task LogsInListsAndOpens()
    {
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_SSH_PORT"] = "2222" });
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        List<string>? callsWhenAsked = null;
        await portal.LoginAsync(Tenant, User, Password, _ => { callsWhenAsked = Calls(fort); return Task.FromResult<string?>(Now()); }, ct.Token);
        // The code is asked once the portal was told to expect it, as on the page.
        Assert.Equal("POST login/get_code", callsWhenAsked!.Last());
        Assert.Equal(2222, portal.SshPort);
        Assert.Equal("127.0.0.1", portal.Host);
        Assert.True(portal.IsSignedIn);

        var first = await portal.ListAsync("", 1, 10, ct.Token);
        Assert.Equal(23, first.Total);
        Assert.Equal(3, first.Pages);
        Assert.Equal(10, first.Assets.Count);
        Assert.Equal("synthetic-01", first.Assets[0].Name);
        Assert.Equal(["root", "$user"], first.Assets[0].Accounts.Select(a => a.Name));
        var third = await portal.ListAsync("", 3, 10, ct.Token);
        Assert.Equal(3, third.Assets.Count);
        var found = await portal.ListAsync("synthetic-05", 1, 10, ct.Token);
        var asset = Assert.Single(found.Assets);

        // A preset account: the portal is asked whether it keeps a password (it does), nothing is asked, "undefined" is sent.
        var ssh = await portal.OpenAsync(asset, asset.Accounts[0], files: false, NoQuestion, ct.Token);
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", ssh.Sid);
        Assert.Equal(("127.0.0.1", 2222, "192.0.2.5"), (ssh.Host, ssh.Port, ssh.Target));
        Assert.Equal([asset.Accounts[0].Id], fort.Stats().GetProperty("noPass").EnumerateArray().Select(x => x.GetString()));
        // $user: the target's own account and password go along; the portal is not asked about it first.
        Assert.True(asset.Accounts[1].AsksForCredentials);
        bool? fixedName = null;
        var files = await portal.OpenAsync(asset, asset.Accounts[1], files: true, (_, f, _) => { fixedName = f; return Task.FromResult<FortCredentials?>(new("root", "synthetic-target")); }, ct.Token);
        Assert.False(fixedName);
        Assert.Equal(ssh.Sid, files.Sid);
        var wrong = await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(asset, asset.Accounts[1], files: false, Answer("root", "nope"), ct.Token));
        Assert.Contains("目标账号或密码错误", wrong.Message);
        var none = await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(asset, asset.Accounts[1], files: false, (_, _, _) => Task.FromResult<FortCredentials?>(null), ct.Token));
        Assert.Contains("没有打开", none.Message);

        var stats = fort.Stats();
        Assert.Empty(stats.GetProperty("badSignature").EnumerateArray());
        Assert.Empty(stats.GetProperty("badToken").EnumerateArray());
        Assert.Empty(stats.GetProperty("unsigned").EnumerateArray());
        Assert.Equal(2, stats.GetProperty("registered").GetArrayLength());
        // In the order of the recorded login, and nothing the recording does not show in between.
        var calls = stats.GetProperty("calls").EnumerateArray().Select(c => c.GetString()!).ToList();
        string[] order = ["GET /fort/login", "POST login/abc", "POST login/login_tenant", "POST login/get_auth", "POST login/get_code", "POST login/do_login", "POST login/isWarn", "isWarn:answer", "POST login/setReload2", "GET /fort/", "GET /fort/operation/sso/sso", "POST sso/get_sshd_port"];
        Assert.Equal(order, calls.Take(order.Length));
        Assert.DoesNotContain("POST login/search_login", calls);
        Assert.All(stats.GetProperty("userAgents").EnumerateArray(), ua => Assert.StartsWith("Forterm-Studio/", ua.GetString()));
        await portal.LogoutAsync(ct.Token);
        Assert.False(portal.IsSignedIn);
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());
        await portal.LogoutAsync(ct.Token);
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());
    }

    /// <summary>The action token rotates with every registered session: two opens at once take turns, and both are registered.</summary>
    [Fact]
    public async Task OpensAtTheSameTimeTakeTurns()
    {
        using var fort = LoopbackFixture.Fort();
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await portal.LoginAsync(Tenant, User, Password, Code, ct.Token);
        var assets = (await portal.ListAsync("", 1, 10, ct.Token)).Assets;
        var all = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => portal.OpenAsync(assets[i], assets[i].Accounts[0], i % 2 == 1, NoQuestion, ct.Token)));
        Assert.Equal(4, all.Length);
        var stats = fort.Stats();
        Assert.Empty(stats.GetProperty("badToken").EnumerateArray());
        Assert.Equal(4, stats.GetProperty("registered").GetArrayLength());
    }

    /// <summary>The recording shows only that the sid is 32 to 36 hex digits and dashes: both shapes are taken, nothing else.</summary>
    [Theory]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", true)]
    [InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E", true)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950", false)]
    [InlineData("synthetic@127.0.0.1", false)]
    public async Task TheSidHasTheRecordedShape(string sid, bool taken)
    {
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_SID"] = sid });
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await portal.LoginAsync(Tenant, User, Password, Code, ct.Token);
        var asset = (await portal.ListAsync("", 1, 10, ct.Token)).Assets[0];
        if (taken) Assert.Equal(sid, (await portal.OpenAsync(asset, asset.Accounts[0], false, NoQuestion, ct.Token)).Sid);
        else Assert.Contains("不是预期的格式", (await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(asset, asset.Accounts[0], false, NoQuestion, ct.Token))).Message);
    }

    /// <summary>Where the portal keeps no password for a preset account, its password is asked (its name is the portal's).</summary>
    [Fact]
    public async Task APresetAccountWithoutPasswordIsAsked()
    {
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_FAIL"] = "no-pass" });
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await portal.LoginAsync(Tenant, User, Password, Code, ct.Token);
        var asset = (await portal.ListAsync("", 1, 10, ct.Token)).Assets[0];
        bool? fixedName = null;
        var ticket = await portal.OpenAsync(asset, asset.Accounts[0], false, (_, f, _) => { fixedName = f; return Task.FromResult<FortCredentials?>(new("whatever", "synthetic-target")); }, ct.Token);
        Assert.True(fixedName);
        Assert.Equal(36, ticket.Sid.Length);
        var empty = await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(asset, asset.Accounts[0], false, Answer("root", ""), ct.Token));
        Assert.Contains("密码不能为空", empty.Message);
    }

    /// <summary>The portal's certificate is shown before anything is sent, and a changed one is refused.</summary>
    [Fact]
    public async Task TheCertificateIsConfirmedFirst()
    {
        using var fort = LoopbackFixture.Fort();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using (var portal = Portal(fort, pin: null))
        {
            var unknown = await Assert.ThrowsAsync<FortCertificateException>(() => portal.LoginAsync(Tenant, User, Password, Code, ct.Token));
            Assert.Equal(fort.Fingerprint, unknown.Fingerprint);
            Assert.False(unknown.Changed);
            Assert.Contains("synthetic fort portal", unknown.Subject);
        }
        using (var portal = Portal(fort, pin: new string('0', 64)))
        {
            var changed = await Assert.ThrowsAsync<FortCertificateException>(() => portal.LoginAsync(Tenant, User, Password, Code, ct.Token));
            Assert.True(changed.Changed);
        }
        var stats = fort.Stats();
        Assert.Equal(0, stats.GetProperty("logins").GetInt32());
        Assert.Empty(stats.GetProperty("calls").EnumerateArray());
        Assert.Throws<FortPortalException>(() => new FortPortal(new Uri("http://127.0.0.1/fort/"), null));
    }

    /// <summary>What the page stops at stops here too; where the portal had already let the login in, it is left again.</summary>
    [Theory]
    [InlineData("password", "登录失败：用户名或密码错误", 0)]
    [InlineData("tenant", "租户名称不对", 0)]
    [InlineData("method", "不提供“用户名+密码+手机APP动态口令”登录", 0)]
    [InlineData("change-password", "要求先修改密码", 1)]
    [InlineData("admin-ip", "没有让这次登录继续：不允许从这个地址登录", 1)]
    [InlineData("session-warn", "其他登录会话", 1)]
    public async Task ARefusedLoginSaysWhy(string fail, string said, int logouts)
    {
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_FAIL"] = fail });
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var e = await Assert.ThrowsAsync<FortPortalException>(() => portal.LoginAsync(Tenant, User, Password, Code, ct.Token));
        Assert.Contains(said, e.Message);
        Assert.False(portal.IsSignedIn);
        Assert.Equal(logouts, fort.Stats().GetProperty("logouts").GetInt32());
        Assert.DoesNotContain("POST login/setReload2", Calls(fort));
    }

    [Fact]
    public async Task AWrongCodeIsRefused()
    {
        using var fort = LoopbackFixture.Fort();
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var e = await Assert.ThrowsAsync<FortPortalException>(() => portal.LoginAsync(Tenant, User, Password, _ => Task.FromResult<string?>("000000"), ct.Token));
        Assert.Contains("登录失败", e.Message);
        await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(new FortAsset("x", "x", "x", "x", "x", []), new FortAccount("a", "root"), false, NoQuestion, ct.Token));
        var none = await Assert.ThrowsAsync<FortPortalException>(() => portal.LoginAsync(Tenant, User, Password, _ => Task.FromResult<string?>(null), ct.Token));
        Assert.Contains("没有输入动态口令", none.Message);
    }

    [Fact]
    public async Task NoSessionIsSaid()
    {
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_FAIL"] = "no-sso" });
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await portal.LoginAsync(Tenant, User, Password, Code, ct.Token);
        var asset = (await portal.ListAsync("", 1, 10, ct.Token)).Assets[0];
        var e = await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(asset, asset.Accounts[0], false, NoQuestion, ct.Token));
        Assert.Contains("没有可用的授权", e.Message);
    }

    /// <summary>What the user types is put into the form unencoded, as the page does: nothing that would add a parameter.</summary>
    [Fact]
    public async Task TypedTextCannotAddParameters()
    {
        using var fort = LoopbackFixture.Fort();
        using var portal = Portal(fort);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var e = await Assert.ThrowsAsync<FortPortalException>(() => portal.LoginAsync(Tenant, "synthetic-user&loginMethod=2", Password, Code, ct.Token));
        Assert.Contains("用户名里有", e.Message);
        Assert.Equal(0, fort.Stats().GetProperty("logins").GetInt32());
        await Assert.ThrowsAsync<FortPortalException>(() => portal.LoginAsync(Tenant, User, Password, _ => Task.FromResult<string?>("12345"), ct.Token));
        await portal.LoginAsync(Tenant, User, Password, Code, ct.Token);
        var asset = (await portal.ListAsync("", 1, 10, ct.Token)).Assets[0];
        var bad = await Assert.ThrowsAsync<FortPortalException>(() => portal.OpenAsync(asset, asset.Accounts[1], false, Answer("root&ssoPwd=x", "p"), ct.Token));
        Assert.Contains("目标主机的账号里有", bad.Message);
    }
}
