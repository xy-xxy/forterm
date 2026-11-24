using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Phase 6: settings, live info column, bastion accounts/TOTP, alpha.36 import, tunnels.</summary>
public sealed class ParityTests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static Color Resource(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is ISolidColorBrush brush ? brush.Color : default;

    [AvaloniaFact]
    public void SettingsApplyPresetAccentScaleAndPersist()
    {
        string data = Paths.TempDirectory();
        try
        {
            var settings = new SettingsViewModel(data);
            settings.SelectThemeCommand.Execute(settings.Presets.Single(p => p.Name == "深蓝"));
            Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
            Assert.Equal(Color.Parse("#0F1A2B"), Resource("St.Window"));
            settings.SelectThemeCommand.Execute(settings.Presets.Single(p => p.Name == "雪白"));
            Assert.Equal(ThemeVariant.Light, Application.Current.ActualThemeVariant);
            Assert.Equal(Color.Parse("#FFFFFF"), Resource("St.Panel"));
            settings.SelectAccentCommand.Execute(settings.Accents.Single(a => a.Color == "#3D7FE0"));
            Assert.Equal(Color.Parse("#3D7FE0"), Resource("St.Accent"));
            settings.UiScalePercent = 120;
            settings.TerminalFontSize = 16;
            var reloaded = Appearance.Load(data);
            Assert.Equal(("雪白", "#3D7FE0", 1.2, 16d), (reloaded.Theme, reloaded.Accent, reloaded.UiScale, reloaded.TerminalFontSize));
            Assert.Single(settings.Presets, p => p.IsSelected);
            var window = new SettingsWindow { DataContext = settings };
            window.Show();
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "settings-theme.png"));
            window.Close();
            settings.FollowSystem = true;
            Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
            settings.ResetCommand.Execute(null);
            Assert.Equal("暗夜", Appearance.Current.Theme);
        }
        finally
        {
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [AvaloniaFact]
    public async Task InfoColumnShowsOnlyOpenSessions()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var open = h.Ssh(fixture.Port, "已打开");
        h.Ssh(fixture.Port, "未打开");
        Dispatcher.UIThread.RunJobs();
        var w = await h.Model.OpenProfileAsync(open);
        await Pump(() => w.Monitor!.Summary != null, TimeSpan.FromSeconds(12));
        // The first sample has no CPU delta yet (shown as unknown, never guessed); wait for the second.
        await Pump(() => h.Model.Home.Rows.Any(r => r.Info.Contains("CPU")), TimeSpan.FromSeconds(12));
        Assert.Contains("CPU", h.Model.Home.Rows.Single(r => r.Name == "已打开").Info);
        Assert.Equal("", h.Model.Home.Rows.Single(r => r.Name == "未打开").Info);
        h.Model.Close(w);
        await Pump(() => !h.Model.Home.Rows.Any(r => r.HasInfo), TimeSpan.FromSeconds(3));
        Assert.All(h.Model.Home.Rows, r => Assert.False(r.HasInfo));
    }

    [AvaloniaFact]
    public async Task AccountsKeepSecretsInVaultAndComputeRfcCodes()
    {
        using var h = new StudioHarness();
        var store = new AccountStore(h.Data);
        // RFC 6238 Appendix B, SHA1, T = 59 s → 94287082.
        var accounts = new AccountsViewModel(store, h.Credentials, h.Interaction, () => DateTimeOffset.FromUnixTimeSeconds(59), startTimer: false);
        Assert.True(accounts.IsEmpty);
        accounts.AddCommand.Execute(null);
        accounts.EditName = "测试堡垒机";
        accounts.EditUrl = "ftp://portal.example";
        accounts.EditUser = "ops";
        accounts.EditPassword = "portal-secret";
        accounts.EditSeed = Totp.EncodeBase32(Encoding.ASCII.GetBytes("12345678901234567890"));
        accounts.EditDigits = AccountsViewModel.DigitChoices.Single(c => c.Value == "8");
        accounts.SaveCommand.Execute(null);
        await Pump(() => accounts.HasError, TimeSpan.FromSeconds(2));
        Assert.Contains("http", accounts.Error);
        accounts.EditUrl = "https://portal.example";
        accounts.SaveCommand.Execute(null);
        await Pump(() => !accounts.IsEditing && accounts.HasSeed, TimeSpan.FromSeconds(3));
        var saved = Assert.Single(store.Accounts);
        Assert.True(saved.HasPassword && saved.HasSeed);
        string json = File.ReadAllText(Path.Combine(h.Data, "accounts.json"));
        Assert.DoesNotContain("portal-secret", json);
        Assert.DoesNotContain(accounts.EditSeed.Length > 0 ? accounts.EditSeed : "GEZDGNBVGY3TQOJQ", json);
        Assert.Equal("••••••••", accounts.Code);
        accounts.ToggleRevealCommand.Execute(null);
        Assert.Equal("94287082", accounts.Code);
        accounts.CopyCodeCommand.Execute(null);
        await Pump(() => h.Interaction.Clipboard.Length > 0, TimeSpan.FromSeconds(2));
        Assert.Equal("94287082", h.Interaction.Clipboard);
        Assert.Equal(("94287082", TimeSpan.FromSeconds(1)), h.Interaction.ClipboardClears.Single());
        accounts.OpenPortalCommand.Execute(null);
        await Pump(() => h.Interaction.OpenedUrls.Count > 0, TimeSpan.FromSeconds(2));
        Assert.Equal("https://portal.example", h.Interaction.OpenedUrls.Single());

        // otpauth URI sets its own parameters.
        accounts.EditCommand.Execute(null);
        accounts.EditSeed = "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&issuer=Example&digits=6&period=30";
        accounts.SaveCommand.Execute(null);
        await Pump(() => !accounts.IsEditing, TimeSpan.FromSeconds(3));
        Assert.Equal(6, store.Accounts.Single().Digits);

        accounts.DeleteCommand.Execute(null);
        await Pump(() => store.Accounts.Count == 0, TimeSpan.FromSeconds(3));
        Assert.Empty(h.Vault.Items.Keys.Where(k => k.StartsWith("studio/account/")));
        accounts.Dispose();
    }

    [Fact]
    public void ImportsAlpha36ExportWithJumpOrderAndGroups()
    {
        string dir = Paths.TempDirectory();
        try
        {
            var store = new LibraryStore(dir);
            string file = Path.Combine(dir, "alpha36.json");
            File.WriteAllText(file, """
            {"Format":"Forterm.Avalonia.Profiles/1","Profiles":[
              {"Name":"目标","Group":"生产","Host":"10.0.0.9","Port":22,"User":"app","Id":"a1","Protocol":"ssh","AuthMode":"keyboard","JumpId":"b2","HasPassword":false},
              {"Name":"二级跳板","Group":"生产","Host":"10.0.0.2","Port":22,"User":"j2","Id":"b2","Protocol":"ssh","JumpId":"c3"},
              {"Name":"一级跳板","Group":"","Host":"203.0.113.5","Port":2222,"User":"j1","Id":"c3","Protocol":"ssh","Favorite":true},
              {"Name":"文件","Group":"文件服务","Host":"192.0.2.8","Port":21,"User":"f","Id":"d4","Protocol":"ftp"}
            ]}
            """);
            Assert.Equal(4, LibraryExchange.Import(store, file));
            var target = store.Profiles.Single(p => p.Name == "目标");
            var second = store.Profiles.Single(p => p.Name == "二级跳板");
            var first = store.Profiles.Single(p => p.Name == "一级跳板");
            Assert.Equal([first.Id, second.Id], target.JumpChain);
            Assert.Equal([first.Id], second.JumpChain);
            Assert.Equal("keyboard", target.AuthMode);
            Assert.Equal("生产", store.GroupPath(target.GroupId));
            Assert.Equal(target.GroupId, second.GroupId);
            Assert.True(first.Favorite);
            Assert.All(store.Profiles, p => Assert.False(p.HasPassword));

            File.WriteAllText(file, """{"Format":"Forterm.Avalonia.Profiles/1","Profiles":[{"Name":"x","Host":"h","User":"u","Password":"leaked"}]}""");
            Assert.Throws<InvalidDataException>(() => LibraryExchange.Import(store, file));
            Assert.Equal(4, store.Profiles.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task TunnelForwardsThroughSavedConnectionAndStops()
    {
        using var jumpHost = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_FORWARD"] = "1" });
        using var target = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var via = h.Ssh(jumpHost.Port, "转发主机");
        int localPort;
        using (var probe = new TcpListener(System.Net.IPAddress.Loopback, 0)) { probe.Start(); localPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        var tunnels = h.Model.Home.Tunnels!;
        h.Interaction.TunnelAnswer = (_, ssh) => new TunnelDefinition { Name = "到目标", ProfileId = ssh.Single().Id, LocalPort = localPort, RemoteHost = "127.0.0.1", RemotePort = target.Port };
        tunnels.NewCommand.Execute(null);
        await Pump(() => tunnels.Rows.Count == 1, TimeSpan.FromSeconds(3));
        var row = tunnels.Rows.Single();
        Assert.Equal("转发主机", row.Via);
        await tunnels.StartAsync(row);
        Assert.True(row.IsRunning, row.Status);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync("127.0.0.1", localPort);
            var buffer = new byte[64];
            using var read = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int n = await client.GetStream().ReadAsync(buffer, read.Token);
            Assert.StartsWith("SSH-2.0", Encoding.ASCII.GetString(buffer, 0, n));
        }
        Assert.Equal(1, jumpHost.Stats().GetProperty("forwarded").GetInt32());

        tunnels.Stop(row.Id, "已停止");
        Assert.False(row.IsRunning);
        await Alpha4Tests.AssertClosedSoon(localPort);

        // The same local port cannot be used twice.
        h.Interaction.TunnelAnswer = (_, ssh) => new TunnelDefinition { Name = "冲突", ProfileId = ssh.Single().Id, LocalPort = localPort, RemoteHost = "127.0.0.1", RemotePort = 22 };
        tunnels.NewCommand.Execute(null);
        await Pump(() => tunnels.HasNotice, TimeSpan.FromSeconds(3));
        Assert.Contains("端口", tunnels.Notice);
    }
}
