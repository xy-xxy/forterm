using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Studio.Launch;
using Forterm.Studio.Library;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Forterm.Studio.Views.Dialogs;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Connection library behaviour and rendering. Loopback fixtures and an in-memory vault only.</summary>
public sealed class LibraryUiTests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Runs the library's queued rebuild after store changes.</summary>
    static void Settle() => Dispatcher.UIThread.RunJobs();

    static void Shot(Avalonia.Controls.Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png"));
    }

    /// <summary>Synthetic sample data for screenshots: documentation addresses only.</summary>
    static void Sample(StudioHarness h)
    {
        var team = h.Store.AddGroup("业务集群", "");
        var sub = h.Store.AddGroup("医联体", team.Id);
        var common = h.Store.AddGroup("常用环境", "");
        string[] names = ["应用1", "应用2", "应用3", "数据存储服务器", "DRG服务器"];
        for (int i = 0; i < names.Length; i++)
            h.Store.Upsert(new StudioProfile { Name = names[i], Host = $"198.51.100.{10 + i}", User = "deploy", GroupId = i < 3 ? sub.Id : team.Id, Note = i == 0 ? "198.51.100.110" : "" });
        h.Store.Upsert(new StudioProfile { Name = "MySQL / Redis", Host = "203.0.113.9", Port = 2222, User = "dba", GroupId = common.Id, Favorite = true, Note = "数据库服务器", LastUsed = DateTimeOffset.UtcNow, UseCount = 4 });
        h.Store.Upsert(new StudioProfile { Name = "麒麟V10 Linux", Host = "192.0.2.45", User = "root", GroupId = common.Id, LastUsed = DateTimeOffset.UtcNow.AddHours(-1), UseCount = 2 });
        h.Store.Upsert(new StudioProfile { Name = "文件服务器", Protocol = "ftp", Port = 21, Host = "192.0.2.80", User = "ftpuser" });
    }

    [AvaloniaFact]
    public async Task TreeCountsSearchSortAndBatchSelection()
    {
        using var h = new StudioHarness();
        Sample(h);
        Settle();
        var lib = h.Model.Home;
        var all = Assert.Single(lib.Tree);
        Assert.Equal(["收藏(1)", "最近(2)", "常用(2)", "业务集群(5)", "常用环境(2)"], all.Children.Select(c => c.Label));
        Assert.Equal(7, lib.Rows.Count); // SSH tab only: the FTP profile is on its own tab
        var cluster = all.Children.Single(c => c.Name == "业务集群");
        Assert.Equal("医联体(3)", cluster.Children[0].Label);
        lib.SelectedNode = cluster;
        Assert.Equal(5, lib.Rows.Count);
        lib.SelectedNode = all;
        lib.Search = "dba@203.0";
        Assert.Equal("MySQL / Redis", Assert.Single(lib.Rows).Name);
        lib.Search = "";
        lib.SortCommand.Execute("Name");
        Assert.Equal(lib.Rows.Select(r => r.Name).OrderBy(n => n, StringComparer.CurrentCulture), lib.Rows.Select(r => r.Name));
        lib.Tabs.Single(t => t.Key == "ftp").SelectCommand!.Execute(null);
        Assert.Equal("文件服务器", Assert.Single(lib.Rows).Name);
        lib.Tabs[0].SelectCommand!.Execute(null);
        lib.Rows[0].IsChecked = true;
        lib.Rows[1].IsChecked = true;
        Assert.Equal("已选 2 项", lib.CheckedLabel);
        lib.DuplicateCheckedCommand.Execute(null);
        Settle();
        Assert.Equal(9, lib.Rows.Count);
        Assert.All(h.Store.Profiles.Where(p => p.Name.EndsWith(" - 副本")), p => Assert.False(p.HasPassword));
    }

    [AvaloniaFact]
    public async Task DeleteConfirmsAndRemovesSavedSecrets()
    {
        using var h = new StudioHarness();
        var p = h.Ssh(2222, "待删除");
        Settle();
        Assert.NotEmpty(h.Vault.Items);
        var row = Assert.Single(h.Model.Home.Rows);
        h.Interaction.ConfirmAnswer = false;
        row.DeleteCommand!.Execute(null);
        await Pump(() => h.Interaction.Confirms.Count > 0, TimeSpan.FromSeconds(2));
        Assert.NotNull(h.Store.Find(p.Id));
        h.Interaction.ConfirmAnswer = true;
        h.Model.Home.Rows[0].DeleteCommand!.Execute(null);
        await Pump(() => h.Store.Find(p.Id) == null, TimeSpan.FromSeconds(2));
        Assert.Null(h.Store.Find(p.Id));
        Assert.Empty(h.Vault.Items);
    }

    [AvaloniaFact]
    public async Task EditorSavesPasswordOnlyToTheVault()
    {
        using var h = new StudioHarness();
        var editor = h.Model.Home.CreateEditor(null);
        editor.Name = "新服务器";
        editor.Host = "192.0.2.77";
        editor.User = "ops";
        editor.Password = "only-in-vault";
        editor.SavePassword = true;
        editor.Port = "abc";
        Assert.False(await editor.SaveAsync());
        Assert.Contains("端口", editor.Error);
        editor.Port = "2200";
        Assert.True(await editor.SaveAsync());
        var saved = Assert.Single(h.Store.Profiles);
        Assert.True(saved.HasPassword);
        Assert.Equal(2200, saved.Port);
        Assert.Equal("only-in-vault", await h.Credentials.PasswordAsync(saved.Id, CancellationToken.None));
        Assert.DoesNotContain("only-in-vault", File.ReadAllText(Path.Combine(h.Data, "library.json")));
        Assert.Equal("", editor.Password);

        var again = h.Model.Home.CreateEditor(saved);
        Assert.True(again.HasSavedPassword);
        again.ForgetPassword = true;
        Assert.True(await again.SaveAsync());
        Assert.False(h.Store.Find(saved.Id)!.HasPassword);
        Assert.Null(await h.Credentials.PasswordAsync(saved.Id, CancellationToken.None));
    }

    [AvaloniaFact]
    public void EditorAdaptsToProtocol()
    {
        using var h = new StudioHarness();
        var editor = h.Model.Home.CreateEditor(null);
        editor.Protocol = ConnectionEditorViewModel.ProtocolChoices.Single(c => c.Value == "telnet");
        Assert.Equal("23", editor.Port);
        Assert.False(editor.UsesPassword);
        Assert.False(editor.IsSsh);
        editor.Protocol = ConnectionEditorViewModel.ProtocolChoices.Single(c => c.Value == "ftp");
        Assert.Equal("21", editor.Port);
        Assert.Equal(["password"], editor.AuthChoices.Select(c => c.Value));
    }

    [AvaloniaFact]
    public async Task SavedConnectionUsesVaultTerminalTypeInitialScriptAndRecordsUse()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var p = h.Ssh(fixture.Port, "保存的连接") with { TerminalType = "vt100", InitialScript = "echo ready\ncd /tmp", DefaultPath = "/" };
        h.Store.Upsert(p);
        var workspace = await h.Model.OpenProfileAsync(p);
        Assert.True(workspace.IsConnected, workspace.Status);
        Assert.True(workspace.HasShell, workspace.Status);
        Assert.Empty(h.Interaction.Challenges); // password came from the vault
        Assert.Equal(1, h.Store.Find(p.Id)!.UseCount);
        await Pump(() => fixture.Stats().GetProperty("input").GetArrayLength() > 0, TimeSpan.FromSeconds(5));
        var stats = fixture.Stats();
        Assert.Contains("vt100", stats.GetProperty("terms").EnumerateArray().Select(t => t.GetString()));
        string typed = Encoding.UTF8.GetString(stats.GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());
        Assert.True(typed.Contains("echo ready\rcd /tmp\r"), "status=" + workspace.Status + " typed=" + typed);
        Settle();
        Assert.Equal("最近(1)", h.Model.Home.Tree[0].Children[1].Label);
    }

    [AvaloniaFact]
    public async Task UnsavedPasswordIsAskedOnceAndNotStored()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness(new ScriptedInteraction(["synthetic-only"]));
        var p = h.Ssh(fixture.Port, "未保存密码", savePassword: false);
        var workspace = await h.Model.OpenProfileAsync(p);
        Assert.True(workspace.IsConnected, workspace.Status);
        var challenge = Assert.Single(h.Interaction.Challenges);
        Assert.False(challenge.Temporary);
        Assert.Empty(h.Vault.Items);
        Assert.False(h.Store.Find(p.Id)!.HasPassword);
    }

    [AvaloniaFact]
    public async Task JumpChainConnectsThroughTheFirstHop()
    {
        using var jumpHost = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_FORWARD"] = "1" });
        using var target = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var jump = h.Ssh(jumpHost.Port, "跳板");
        var p = h.Ssh(target.Port, "内网目标", true, jump.Id);
        var workspace = await h.Model.OpenProfileAsync(p);
        Assert.True(workspace.IsConnected, workspace.Status);
        Assert.Equal(1, jumpHost.Stats().GetProperty("forwarded").GetInt32());
        Assert.Equal(2, h.Interaction.HostKeys.Count);
    }

    [AvaloniaFact]
    public async Task TestConnectionDoesNotTrustTheHostKey()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var editor = h.Model.Home.CreateEditor(null);
        editor.Host = "127.0.0.1";
        editor.Port = fixture.Port.ToString();
        editor.User = "synthetic";
        editor.Password = "synthetic-only";
        editor.TestCommand.Execute(null);
        await Pump(() => !editor.IsTesting && editor.TestResult.Length > 0 && editor.TestResult != "正在测试…", TimeSpan.FromSeconds(20));
        Assert.StartsWith("SSH 认证成功", editor.TestResult);
        Assert.Single(h.Interaction.HostKeys);
        Assert.Empty(h.Store.Profiles);
        // A real connection afterwards still asks: the test did not write the trust store.
        var p = h.Ssh(fixture.Port);
        await h.Model.OpenProfileAsync(p);
        Assert.Equal(2, h.Interaction.HostKeys.Count);
    }

    /// <summary>Regression: a workspace that connects while another tab is shown (batch "并发",
    /// a launch arriving in the background) has no terminal template yet. Its terminal must attach
    /// when the tab is shown instead of failing with "终端通道不可用".</summary>
    [AvaloniaFact]
    public async Task BackgroundWorkspaceTerminalAttachesWhenShown()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var first = h.Ssh(fixture.Port, "后台一");
        var second = h.Ssh(fixture.Port, "前台二");
        var window = new MainWindow { Width = 1200, Height = 760, DataContext = h.Model };
        window.Show();
        try
        {
            // Like "并发": both open at once, so the first tab is never shown while it connects.
            var opening = new[] { h.Model.OpenProfileAsync(first), h.Model.OpenProfileAsync(second) };
            var opened = await Task.WhenAll(opening);
            var (background, foreground) = (opened[0], opened[1]);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(background.HasShell, background.Status);
            Assert.DoesNotContain("不可用", background.Status);
            Assert.False(foreground.Terminal!.HasPendingConnection);
            Assert.True(background.Terminal!.HasPendingConnection);
            h.Model.Selected = background;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(background.Terminal.HasPendingConnection);
            Assert.True(background.Terminal.IsLive);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LibraryAndEditorRender()
    {
        using var h = new StudioHarness();
        Sample(h);
        Settle();
        var window = new MainWindow { Width = 1440, Height = 860, DataContext = h.Model };
        window.Show();
        try
        {
            await Pump(() => h.Model.Home.Rows.All(r => r.Latency.Length > 0), TimeSpan.FromSeconds(8));
            Shot(window, "library-dark");
            h.Model.Home.Rows[1].IsChecked = true;
            h.Model.Home.Rows[2].IsChecked = true;
            Shot(window, "library-batch-selection");
            var editor = h.Model.Home.CreateEditor(h.Store.Profiles.First(p => p.Name == "MySQL / Redis"));
            var dialog = new ConnectionEditorDialog { DataContext = editor };
            dialog.Show();
            Shot(dialog, "connection-editor-basic");
            var navButtons = dialog.FindControl<StackPanel>("Nav")!.Children.OfType<Button>().ToList();
            Assert.Equal(["SectionBasic"], navButtons.Where(b => b.Classes.Contains("active")).Select(b => (string)b.Tag!));
            dialog.FindControl<Avalonia.Controls.ScrollViewer>("Scroller")!.Offset = new Avalonia.Vector(0, 100000);
            Shot(dialog, "connection-editor-init");
            Assert.Equal(["SectionInit"], navButtons.Where(b => b.Classes.Contains("active")).Select(b => (string)b.Tag!));
            dialog.Close();
        }
        finally { window.Close(); }
    }
}
