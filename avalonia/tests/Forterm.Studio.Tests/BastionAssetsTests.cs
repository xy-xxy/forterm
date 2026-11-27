using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services.Bastion;
using Forterm.Studio.ViewModels;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>The library's "堡垒机" tab: an account logs into the stand-in fort portal without a browser and opens the
/// sessions it hands out in Studio, as the vendor's client would start Xshell and Xftp (the sid as the user, two apostrophes
/// as the password). Everything is on 127.0.0.1.</summary>
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
        // "登录堡垒机" shows the library's "堡垒机" tab with that account, and logs in there.
        Assert.Same(h.Model.Home, h.Model.Selected);
        Assert.True(h.Model.Home.IsBastionTab);
        Assert.Equal(account.Id, h.Model.Bastions.Selected?.Id);
        return h.Model.Bastions.Current ?? throw new InvalidOperationException("没有堡垒机资产");
    }

    [AvaloniaFact]
    public async Task AnAccountLogsInAndOpensSessions()
    {
        using var ssh = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_ACCEPT_USER"] = Sid, ["FIXTURE_ACCEPT_PASSWORD"] = "''" });
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_SSH_PORT"] = ssh.Port.ToString(), ["FORT_SID"] = Sid });
        using var h = new StudioHarness(new ScriptedInteraction(["root", "synthetic-target"]));
        var account = await Account(h, fort);
        var assets = Open(h, account);
        await Pump(() => assets.IsLoggedIn && assets.Assets.Count > 0);
        Assert.True(h.Model.Bastions.Selected!.IsLoggedIn);
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
        Assert.Equal("account:root", row.Choice!.Key);
        Assert.Equal(["account:root", "ask"], row.Choices.Select(c => c.Key));

        row.SshCommand.Execute(null);
        await Pump(() => h.Model.Workspaces.Any(w => w.Title == "synthetic-05" && w.IsConnected) && !assets.IsBusy);
        var terminal = h.Model.Workspaces.Single(w => w.Title == "synthetic-05");
        Assert.True(terminal.IsConnected, terminal.Status);
        Assert.Contains("已打开 synthetic-05（SSH）", assets.Status);

        // $user: the target's own account and password are asked, and go to the portal, not to the session.
        row.Choice = row.Choices.Single(c => c.Key == "ask");
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
        new(account, h.Model.Accounts, h.Credentials, h.Interaction, (t, files, title) => { opened.Add((t, files)); return Task.FromResult($"已打开 {title}（{(files ? "SFTP" : "SSH")}）"); });

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
        h.Interaction.ConfirmAnswer = false;
        var assets = Open(h, account);
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
        Assert.False(h.Model.Home.IsBastionTab);
        Assert.Contains("租户名称", accounts.Error);
        Assert.Throws<InvalidDataException>(() => AccountStore.Validate(account with { TenantName = "a&b" }));
        Assert.Throws<InvalidDataException>(() => AccountStore.Validate(account with { CertificatePin = "xyz" }));
    }

    /// <summary>"目标主机凭据": a target login kept once is chosen in a machine's account list and opens it without a question
    /// (its password from the credential store, sent as the page sends it). The choice is remembered for the machine; a
    /// kept login only for the machines it was chosen for, anything else also as the first choice of machines not chosen
    /// for yet. Removing the login removes its password and the choices of it.</summary>
    [AvaloniaFact]
    public async Task AKeptLoginIsChosenAndRemembered()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var opened = new List<(FortTicket, bool)>();
        var account = await Account(h, fort, pin: fort.Fingerprint);
        using var assets = Direct(h, account, opened);
        await LogIn(assets);
        void Sync() { Dispatcher.UIThread.RunJobs(); assets.Update(h.Model.Accounts.Find(account.Id)!); }

        var logins = assets.Logins;
        logins.AddCommand.Execute(null);
        logins.EditName = "运维";
        logins.EditUser = "root&x";
        logins.EditPassword = "synthetic-target";
        logins.SaveCommand.Execute(null);
        await Pump(() => logins.HasError);
        Assert.Contains("不能有 & = % + #", logins.Error);
        logins.EditUser = "root";
        logins.SaveCommand.Execute(null);
        await Pump(() => !logins.IsEditing);
        Sync();
        var kept = Assert.Single(h.Model.Accounts.Find(account.Id)!.Logins);
        Assert.Equal(("运维", "root"), (kept.Name, kept.User));
        Assert.Equal("synthetic-target", await h.Credentials.AccountSecretAsync(kept.Id, BastionLogin.SecretKind, CancellationToken.None));
        Assert.DoesNotContain("synthetic-target", File.Exists(Path.Combine(h.Data, "accounts.json")) ? File.ReadAllText(Path.Combine(h.Data, "accounts.json")) : "");

        var row = assets.Assets[4];
        Assert.Equal(["account:root", "login:" + kept.Id, "ask"], row.Choices.Select(c => c.Key));
        Assert.Equal("运维（root）", row.Choices[1].Label);
        row.Choice = row.Choices[1];
        Sync();
        var choices = h.Model.Accounts.Find(account.Id)!.Choices;
        Assert.Equal("login:" + kept.Id, choices[row.Asset.Id]);
        Assert.False(choices.ContainsKey("*"));
        // Opened with it: nothing asked; the fixture checks the target's account and password.
        row.SftpCommand.Execute(null);
        await Pump(() => !assets.IsBusy);
        Assert.Contains("已打开", assets.Status);
        Assert.Single(opened);
        Assert.Empty(h.Interaction.Challenges);
        var target = Assert.Single(fort.Stats().GetProperty("targets").EnumerateArray());
        Assert.StartsWith("usr", target.GetProperty("account").GetString());

        // "每次输入" for another machine: that one is the first choice of machines not chosen for yet.
        assets.Assets[1].Choice = assets.Assets[1].Choices.Single(c => c.Key == "ask");
        Sync();
        // Read again: each machine keeps its choice; the others start with "ask", never with the kept login.
        assets.RefreshCommand.Execute(null);
        await Pump(() => !assets.IsBusy);
        Assert.Equal("login:" + kept.Id, assets.Assets[4].Choice!.Key);
        Assert.Equal("ask", assets.Assets[1].Choice!.Key);
        Assert.Equal("ask", assets.Assets[0].Choice!.Key);
        // A null from the list is no choice.
        assets.Assets[0].Choice = null;
        Assert.Equal("ask", assets.Assets[0].Choice!.Key);

        // Editing the account in "堡垒机账户" keeps them.
        h.Model.Home.AccountsCommand!.Execute(null);
        var manage = h.Interaction.AccountsShown!;
        manage.Selected = manage.Accounts.Single(a => a.Id == account.Id);
        manage.EditCommand.Execute(null);
        manage.EditNote = "改了备注";
        manage.SaveCommand.Execute(null);
        await Pump(() => !manage.IsEditing);
        Assert.Single(h.Model.Accounts.Find(account.Id)!.Logins);
        Assert.Equal(3, h.Model.Accounts.Find(account.Id)!.Choices.Count);

        // Removed: its password goes, and the machines that had it go back to the portal's account.
        logins.DeleteCommand.Execute(kept);
        await Pump(() => logins.IsEmpty);
        Sync();
        Assert.Null(await h.Credentials.AccountSecretAsync(kept.Id, BastionLogin.SecretKind, CancellationToken.None));
        Assert.DoesNotContain(h.Model.Accounts.Find(account.Id)!.Choices.Values, v => v.StartsWith("login:", StringComparison.Ordinal));
        Assert.Equal("ask", assets.Assets[4].Choice!.Key);
        Assert.Equal(["account:root", "ask"], assets.Assets[4].Choices.Select(c => c.Key));
    }

    /// <summary>What the review of alpha.14 found: an account removed while a target login is saved stays removed (and the
    /// password just written goes); a certificate trusted while the portal address was edited is not kept for the new one;
    /// another portal forgets the choices made for the old one's machines.</summary>
    [AvaloniaFact]
    public async Task EditsMeanwhileDoNotBringBackOrMisplace()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var opened = new List<(FortTicket, bool)>();
        var account = await Account(h, fort);
        using var assets = Direct(h, account, opened);

        // The certificate question is open while the address is changed in "堡垒机账户".
        string moved = $"https://127.0.0.2:{fort.Port}/fort/login";
        // As the tab does with a change of the account: the model is told.
        h.Interaction.OnConfirm = _ => { h.Model.Accounts.Upsert(h.Model.Accounts.Find(account.Id)! with { PortalUrl = moved }); assets.Update(h.Model.Accounts.Find(account.Id)!); };
        assets.LoginCommand.Execute(null);
        await Pump(() => assets.IsLoggedIn && !assets.IsBusy);
        h.Interaction.OnConfirm = null;
        Assert.Equal("", h.Model.Accounts.Find(account.Id)!.CertificatePin);

        // Another portal in "堡垒机账户": the choices go, the logins stay.
        h.Model.Accounts.Upsert(h.Model.Accounts.Find(account.Id)! with { PortalUrl = account.PortalUrl, Logins = [new BastionLogin { User = "root" }], Choices = new Dictionary<string, string> { ["res1"] = "ask", ["*"] = "ask" } });
        h.Model.Home.AccountsCommand!.Execute(null);
        var manage = h.Interaction.AccountsShown!;
        manage.Selected = manage.Accounts.Single(a => a.Id == account.Id);
        manage.EditCommand.Execute(null);
        manage.EditUrl = moved;
        manage.SaveCommand.Execute(null);
        await Pump(() => !manage.IsEditing);
        Assert.Empty(h.Model.Accounts.Find(account.Id)!.Choices);
        Assert.Single(h.Model.Accounts.Find(account.Id)!.Logins);

        // Removed while a login's password is written: it stays removed.
        assets.Logins.AddCommand.Execute(null);
        assets.Logins.EditUser = "admin";
        assets.Logins.EditPassword = "synthetic-target";
        h.Vault.OnWrite = _ => h.Model.Accounts.Remove(account.Id);
        assets.Logins.SaveCommand.Execute(null);
        await Pump(() => assets.Logins.HasError);
        h.Vault.OnWrite = null;
        Assert.Contains("已经不在了", assets.Logins.Error);
        Assert.Null(h.Model.Accounts.Find(account.Id));
        Assert.DoesNotContain(h.Vault.Items.Keys, k => k.Contains("target", StringComparison.Ordinal));
    }

    /// <summary>A plain close goes through the leaving too, and should the window stay, the tab works on.</summary>
    [AvaloniaFact]
    public async Task APlainCloseLeavesAndTheTabWorksOn()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        Assert.False(h.Model.NeedsCloseWork);
        var assets = Open(h, await Account(h, fort, pin: fort.Fingerprint));
        await Pump(() => assets.IsLoggedIn && !assets.IsBusy);
        Assert.False(h.Model.NeedsCloseConfirmation);
        Assert.True(h.Model.NeedsCloseWork);
        var leaving = h.Model.Bastions.LeaveAllAsync(TimeSpan.FromSeconds(10));
        await Pump(() => leaving.IsCompleted, 15);
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());
        Assert.False(assets.IsLoggedIn);
        Assert.False(h.Model.Bastions.HasLogins);
        var again = h.Model.Bastions.Current!;
        Assert.NotSame(assets, again);
        again.LoginCommand.Execute(null);
        await Pump(() => again.IsLoggedIn && !again.IsBusy);
        Assert.True(again.IsLoggedIn, again.Status);
    }

    /// <summary>The tab follows "堡垒机账户": accounts added, edited and removed; removing one that is logged in leaves its
    /// portal. Nothing is sent before "登录".</summary>
    [AvaloniaFact]
    public async Task TheTabFollowsTheAccounts()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var hub = h.Model.Bastions;
        Assert.True(hub.IsEmpty);
        Assert.Null(hub.Current);
        var account = await Account(h, fort, pin: fort.Fingerprint);
        var other = new BastionAccount { Name = "另一个", PortalUrl = "http://bastion.example/", Username = "u" };
        h.Model.Accounts.Upsert(other);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["合成堡垒机", "另一个"], hub.Items.Select(i => i.Name));
        Assert.Equal(account.Id, hub.Selected!.Id);
        Assert.True(hub.Items[0].CanLogIn);
        Assert.False(hub.Items[1].CanLogIn);
        hub.Selected = null;
        Assert.Equal(account.Id, hub.Selected!.Id);
        hub.Selected = hub.Items[1];
        Assert.True(hub.Current!.IsLoggedOut);
        Assert.Equal(0, fort.Stats().GetProperty("calls").GetArrayLength());

        hub.Selected = hub.Items[0];
        hub.Current!.LoginCommand.Execute(null);
        await Pump(() => hub.Current!.IsLoggedIn && !hub.Current.IsBusy);
        Assert.True(hub.Items[0].IsLoggedIn);
        h.Model.Accounts.Upsert(h.Model.Accounts.Find(account.Id)! with { Name = "改名的堡垒机" });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("改名的堡垒机", hub.Items[0].Name);
        Assert.True(hub.Current!.IsLoggedIn);

        h.Model.Accounts.Remove(account.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["另一个"], hub.Items.Select(i => i.Name));
        Assert.Equal(other.Id, hub.Selected!.Id);
        var end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end && fort.Stats().GetProperty("logouts").GetInt32() == 0) { Dispatcher.UIThread.RunJobs(); await Task.Delay(50); }
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());
    }

    /// <summary>Closing the program leaves every portal that is logged in.</summary>
    [AvaloniaFact]
    public async Task ClosingTheProgramLeavesThePortals()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var assets = Open(h, await Account(h, fort, pin: fort.Fingerprint));
        await Pump(() => assets.IsLoggedIn && !assets.IsBusy);
        var closing = h.Model.ConfirmCloseAllAsync();
        await Pump(() => closing.IsCompleted, 15);
        Assert.True(closing.Result);
        Assert.Equal(1, fort.Stats().GetProperty("logouts").GetInt32());
    }

    /// <summary>A bastion that leaves the file channel of a terminal route unanswered (seen 2026-11-27): the terminal is
    /// there at once, the tab says "已打开" then, and the session says what it is still waiting for.</summary>
    [AvaloniaFact]
    public async Task OpenIsSaidWhenTheTerminalIsThere()
    {
        using var ssh = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_ACCEPT_USER"] = Sid, ["FIXTURE_ACCEPT_PASSWORD"] = "''", ["FIXTURE_SFTP_SILENT"] = "1" });
        using var fort = LoopbackFixture.Fort(new Dictionary<string, string> { ["FORT_SSH_PORT"] = ssh.Port.ToString(), ["FORT_SID"] = Sid });
        using var h = new StudioHarness();
        var assets = Open(h, await Account(h, fort, pin: fort.Fingerprint));
        await Pump(() => assets.IsLoggedIn && assets.Assets.Count > 0 && !assets.IsBusy);
        assets.Assets[0].SshCommand.Execute(null);
        await Pump(() => !assets.IsBusy, 20);
        Assert.Equal("已打开 synthetic-01（SSH）", assets.Status);
        var session = h.Model.Workspaces.Single(w => w.Title == "synthetic-01");
        Assert.True(session.HasShell);
        Assert.False(session.HasFiles);
        Assert.Equal("终端已打开，正在打开文件通道…", session.Status);
        foreach (var w in h.Model.Workspaces.ToList()) h.Model.Close(w);
    }

    /// <summary>The tab, drawn (software rendering) inside the main window: no account, an account before its login, and
    /// the machines after it; rows, accounts, buttons and pages fit.</summary>
    [AvaloniaFact]
    public async Task TheTabShowsTheMachines()
    {
        using var fort = LoopbackFixture.Fort();
        using var h = new StudioHarness();
        var window = new Forterm.Studio.Views.MainWindow { Width = 1280, Height = 760, DataContext = h.Model };
        window.Show();
        void Settle() { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }
        try
        {
            h.Model.Home.ShowBastions();
            Settle();
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "bastion-tab-empty.png"));
            await Account(h, fort, pin: fort.Fingerprint);
            Dispatcher.UIThread.RunJobs();
            Settle();
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "bastion-tab-login.png"));
            var assets = h.Model.Bastions.Current!;
            assets.LoginCommand.Execute(null);
            await Pump(() => assets.IsLoggedIn && assets.Assets.Count > 0 && !assets.IsBusy);
            Settle();
            var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>().Where(b => b.Content as string == "SSH" && b.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(buttons);
            Assert.All(buttons.Take(5), b => Assert.True(Avalonia.VisualExtensions.TranslatePoint(b, new Avalonia.Point(b.Bounds.Width, 0), window)!.Value.X <= window.Bounds.Width, "SSH button outside the window"));
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "bastion-tab-machines.png"));
            // "目标凭据": one kept, chosen for a machine; the panel beside the list.
            assets.Logins.IsOpen = true;
            assets.Logins.AddCommand.Execute(null);
            assets.Logins.EditName = "运维";
            assets.Logins.EditUser = "root";
            assets.Logins.EditPassword = "synthetic-target";
            assets.Logins.SaveCommand.Execute(null);
            await Pump(() => !assets.Logins.IsEditing);
            Dispatcher.UIThread.RunJobs();
            var row = assets.Assets[1];
            row.Choice = row.Choices.First(c => c.IsSaved);
            Settle();
            Assert.Single(assets.Logins.Items);
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "bastion-tab-logins.png"));
        }
        finally { window.Close(); }
    }
}
