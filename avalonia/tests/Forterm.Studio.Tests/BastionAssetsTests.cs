using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services.Bastion;
using Forterm.Studio.ViewModels;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>"堡垒机资产": an account logs into the stand-in fort portal without a browser and opens the sessions it hands out
/// in Studio, as the vendor's client would start Xshell and Xftp (the sid as the user, two apostrophes as the password).
/// Everything is on 127.0.0.1.</summary>
public sealed class BastionAssetsTests
{
    const string Sid = "0f8fad5b-d9cb-469f-a165-70867728950e";

    static async Task Pump(Func<bool> until, int seconds = 15)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<BastionAccount> Account(StudioHarness h, LoopbackFixture fort, string pin = "")
    {
        var account = new BastionAccount { Name = "合成堡垒机", PortalUrl = $"https://127.0.0.1:{fort.Port}/fort/login", TenantName = "synthetic-tenant", Username = "synthetic-user", HasPassword = true, HasSeed = true, CertificatePin = pin };
        await h.Credentials.SaveAccountSecretAsync(account.Id, "password", "synthetic-password", CancellationToken.None);
        await h.Credentials.SaveAccountSecretAsync(account.Id, "seed", "JBSWY3DPEHPK3PXP", CancellationToken.None);
        h.Model.Accounts.Upsert(account);
        return account;
    }

    static BastionAssetsViewModel Open(StudioHarness h, BastionAccount account)
    {
        h.Model.Home.AccountsCommand!.Execute(null);
        var accounts = h.Interaction.AccountsShown!;
        accounts.Selected = accounts.Accounts.Single(a => a.Id == account.Id);
        Assert.True(accounts.CanLogIn);
        accounts.LoginCommand.Execute(null);
        return h.Interaction.BastionShown ?? throw new InvalidOperationException("没有打开堡垒机资产");
    }

    [AvaloniaFact]
    public async Task AnAccountLogsInAndOpensSessions()
    {
        using var ssh = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_ACCEPT_USER"] = Sid, ["FIXTURE_ACCEPT_PASSWORD"] = "''" });
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_SSH_PORT"] = ssh.Port.ToString(), ["FORT_SID"] = Sid });
        using var h = new StudioHarness(new ScriptedInteraction(["root", "synthetic-target"]));
        var account = await Account(h, fort);
        using var assets = Open(h, account);
        assets.LoginCommand.Execute(null);
        await Pump(() => assets.IsLoggedIn && assets.Assets.Count > 0);
        Assert.True(assets.IsLoggedIn, assets.Status);
        // The certificate was shown once and is kept with the account.
        var asked = Assert.Single(h.Interaction.Confirms, c => c.StartsWith("确认堡垒机网页的证书：", StringComparison.Ordinal));
        Assert.Contains("SHA-256 指纹", asked);
        Assert.Equal(fort.Fingerprint, h.Model.Accounts.Find(account.Id)!.CertificatePin);
        Assert.Equal(BastionAssetsViewModel.PageSize, assets.Assets.Count);
        Assert.Equal("第 1 / 2 页，共 23 台", assets.PageText);
        assets.NextCommand.Execute(null);
        await Pump(() => assets.PageText.StartsWith("第 2 /", StringComparison.Ordinal));
        Assert.Equal(3, assets.Assets.Count);
        assets.Query = "synthetic-05";
        assets.SearchCommand.Execute(null);
        await Pump(() => assets.Assets.Count == 1);
        var row = Assert.Single(assets.Assets);
        Assert.Equal("root", row.Account!.Name);

        row.SshCommand.Execute(null);
        await Pump(() => h.Model.Workspaces.Any(w => w.Title == "synthetic-05" && w.IsConnected) && !assets.IsBusy);
        var terminal = h.Model.Workspaces.Single(w => w.Title == "synthetic-05");
        Assert.True(terminal.IsConnected, terminal.Status);
        Assert.Contains("已打开 synthetic-05（SSH）", assets.Status);

        // $user: the target's own account and password are asked, and go to the portal, not to the session.
        row.Account = row.Accounts.Single(a => a.AsksForCredentials);
        row.SftpCommand.Execute(null);
        await Pump(() => h.Model.Workspaces.Count(w => w.Title == "synthetic-05") == 2 && h.Model.Workspaces.All(w => w.IsConnected));
        Assert.Equal(2, h.Model.Workspaces.Count(w => w.Title == "synthetic-05"));
        Assert.Contains(h.Interaction.Challenges, c => c.Questions.Select(q => q.Text).SequenceEqual(["账号", "密码"]));

        var accepted = ssh.Stats().GetProperty("acceptedUsers").EnumerateArray().Select(u => u.GetString()).ToList();
        Assert.Equal([Sid, Sid], accepted);
        var portal = fort.Stats();
        Assert.Equal(2, portal.GetProperty("registered").GetArrayLength());
        Assert.Empty(portal.GetProperty("badSignature").EnumerateArray());
        Assert.Empty(portal.GetProperty("badToken").EnumerateArray());

        assets.LogoutCommand.Execute(null);
        await Pump(() => !assets.IsLoggedIn);
        Assert.Equal("已退出登录", assets.Status);
        foreach (var w in h.Model.Workspaces.ToList()) h.Model.Close(w);
    }

    /// <summary>The window's model without the main window: sessions it would open are only recorded.</summary>
    static BastionAssetsViewModel Direct(StudioHarness h, BastionAccount account, List<(FortTicket Ticket, bool Files)> opened) =>
        new(account, h.Model.Accounts, h.Credentials, h.Interaction, (t, files, _) => { opened.Add((t, files)); return Task.CompletedTask; });

    static async Task LogIn(BastionAssetsViewModel assets)
    {
        assets.LoginCommand.Execute(null);
        await Pump(() => assets.IsLoggedIn && assets.Assets.Count > 0 && !assets.IsBusy);
        Assert.True(assets.IsLoggedIn, assets.Status);
    }

    /// <summary>What the portal says when it hands out no session reaches the window, and the login stays.</summary>
    [AvaloniaFact]
    public async Task ARowThatCannotOpenSaysWhy()
    {
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_FAIL"] = "no-sso" });
        using var h = new StudioHarness();
        var opened = new List<(FortTicket, bool)>();
        using var assets = Direct(h, await Account(h, fort, pin: fort.Fingerprint), opened);
        await LogIn(assets);
        assets.Assets[0].SshCommand.Execute(null);
        await Pump(() => !assets.IsBusy && assets.Status.Contains("没有可用的授权"));
        Assert.Contains("堡垒机没有给出会话：没有可用的授权", assets.Status);
        Assert.True(assets.IsLoggedIn);
        Assert.Empty(opened);
    }

    /// <summary>One thing at a time: a second click while a session opens is not sent to the portal.</summary>
    [AvaloniaFact]
    public async Task ASecondClickWhileOpeningIsIgnored()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var opened = new List<(FortTicket, bool)>();
        using var assets = Direct(h, await Account(h, fort, pin: fort.Fingerprint), opened);
        await LogIn(assets);
        assets.Assets[0].SshCommand.Execute(null);
        Assert.True(assets.IsBusy);
        Assert.False(assets.IsIdle);
        assets.Assets[1].SftpCommand.Execute(null);
        assets.NextCommand.Execute(null);
        await Pump(() => !assets.IsBusy);
        var single = Assert.Single(opened);
        Assert.False(single.Item2);
        Assert.Contains("已打开 synthetic-01（SSH）", assets.Status);
        Assert.StartsWith("第 1 /", assets.PageText);
        var stats = fort.Stats();
        Assert.Equal(1, stats.GetProperty("registered").GetArrayLength());
        Assert.Empty(stats.GetProperty("badToken").EnumerateArray());
    }

    /// <summary>The certificate is confirmed once: logging in again in the same window takes the pin that was just kept. The
    /// code (no seed kept) is asked only after the certificate.</summary>
    [AvaloniaFact]
    public async Task TheCertificateIsAskedOnceAndTheCodeAfterIt()
    {
        using var fort = LoopbackFixture.Fort();
        string code = Totp.Generate(Totp.DecodeBase32("JBSWY3DPEHPK3PXP"), new TotpParameters(), DateTimeOffset.UtcNow).Code;
        using var h = new StudioHarness(new ScriptedInteraction([code]));
        var account = await Account(h, fort);
        account = account with { HasSeed = false };
        h.Model.Accounts.Upsert(account);
        int? questionsAtConfirm = null;
        h.Interaction.OnConfirm = _ => questionsAtConfirm = h.Interaction.Challenges.Count;
        var opened = new List<(FortTicket, bool)>();
        using var assets = Direct(h, account, opened);
        await LogIn(assets);
        Assert.Equal(0, questionsAtConfirm);
        var asked = Assert.Single(h.Interaction.Challenges);
        Assert.Equal("动态口令", asked.Questions.Single().Text);

        assets.LogoutCommand.Execute(null);
        await Pump(() => !assets.IsLoggedIn && !assets.IsBusy);
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());
        h.Model.Accounts.Upsert(h.Model.Accounts.Find(account.Id)! with { HasSeed = true });
        await LogIn(assets);
        Assert.Single(h.Interaction.Confirms, c => c.StartsWith("确认堡垒机网页的证书", StringComparison.Ordinal));
        Assert.Single(h.Interaction.Challenges);
    }

    /// <summary>Closing the window leaves the portal, also while something is still running.</summary>
    [AvaloniaFact]
    public async Task ClosingTheWindowLeavesThePortal()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var opened = new List<(FortTicket, bool)>();
        var account = await Account(h, fort, pin: fort.Fingerprint);
        var assets = Direct(h, account, opened);
        await LogIn(assets);
        assets.NextCommand.Execute(null);
        assets.Dispose();
        var end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end && fort.Stats().GetProperty("logouts").GetInt32() == 0) { Dispatcher.UIThread.RunJobs(); await Task.Delay(50); }
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());

        // A login that is still on its way when the window closes is left as well.
        var second = Direct(h, account, opened);
        second.LoginCommand.Execute(null);
        await Pump(() => fort.Stats().GetProperty("logins").GetInt32() == 2, 15);
        second.Dispose();
        end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end && fort.Stats().GetProperty("logouts").GetInt32() < 2) { Dispatcher.UIThread.RunJobs(); await Task.Delay(50); }
        Assert.Equal(2, fort.Stats().GetProperty("logouts").GetInt32());
    }

    /// <summary>A certificate other than the one trusted: said as a possible impostor, and nothing is sent when refused.</summary>
    [AvaloniaFact]
    public async Task AChangedCertificateIsRefused()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var account = await Account(h, fort, pin: new string('a', 64));
        using var assets = Open(h, account);
        h.Interaction.ConfirmAnswer = false;
        assets.LoginCommand.Execute(null);
        await Pump(() => assets.Status.Contains("没有信任"));
        Assert.False(assets.IsLoggedIn);
        Assert.Contains(h.Interaction.Confirms, c => c.StartsWith("证书变了：", StringComparison.Ordinal) && c.Contains("可能有人在冒充堡垒机"));
        Assert.Equal(new string('a', 64), h.Model.Accounts.Find(account.Id)!.CertificatePin);
        Assert.Equal(0, fort.Stats().GetProperty("logins").GetInt32());
    }

    /// <summary>Without a tenant or an https address, logging in is not offered as if it could work.</summary>
    [AvaloniaFact]
    public void AnAccountWithoutATenantIsTold()
    {
        using var h = new StudioHarness();
        var account = new BastionAccount { Name = "旧账户", PortalUrl = "https://bastion.example/", Username = "u" };
        h.Model.Accounts.Upsert(account);
        h.Model.Home.AccountsCommand!.Execute(null);
        var accounts = h.Interaction.AccountsShown!;
        accounts.Selected = accounts.Accounts.Single();
        accounts.LoginCommand.Execute(null);
        Assert.Null(h.Interaction.BastionShown);
        Assert.Contains("租户名称", accounts.Error);
        Assert.Throws<InvalidDataException>(() => AccountStore.Validate(account with { TenantName = "a&b" }));
        Assert.Throws<InvalidDataException>(() => AccountStore.Validate(account with { CertificatePin = "xyz" }));
    }

    /// <summary>The window, drawn (software rendering) after a login: rows, accounts, buttons and paging fit.</summary>
    [AvaloniaFact]
    public async Task TheWindowShowsTheAssets()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var account = await Account(h, fort, pin: fort.Fingerprint);
        var assets = Open(h, account);
        var window = new Forterm.Studio.Views.BastionAssetsWindow { DataContext = assets, Width = 900, Height = 600 };
        window.Show();
        try
        {
            assets.LoginCommand.Execute(null);
            await Pump(() => assets.IsLoggedIn && assets.Assets.Count > 0);
            for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal("堡垒机资产 · 合成堡垒机", window.Title);
            var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>().Where(b => b.Content as string == "SSH").ToList();
            Assert.NotEmpty(buttons);
            Assert.All(buttons.Take(5), b => Assert.True(b.Bounds.Right <= window.Bounds.Width, "SSH button outside the window"));
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "alpha13-bastion-assets.png"));
        }
        finally { window.Close(); }
    }
}
