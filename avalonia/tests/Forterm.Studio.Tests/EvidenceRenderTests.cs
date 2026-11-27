using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Software-rendered screenshots of the newer screens for side-by-side review.
/// Synthetic data only (documentation addresses, loopback fixtures).</summary>
public sealed class EvidenceRenderTests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static void Shot(Avalonia.Controls.Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png"));
    }

    [AvaloniaFact]
    public async Task EditorTabRenders()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1440, Height = 860, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "编辑截图"));
            string local = Path.Combine(h.Data, "application.yml");
            File.WriteAllText(local, "server:\n  port: 8080\nspring:\n  profiles:\n    active: prod\n# 注释：保存前会确认并核对远程版本\n");
            h.Interaction.OpenFiles = [local];
            w.SelectedNode = null;
            w.UploadFilesCommand.Execute(null);
            await Pump(() => w.Transfers!.Visible.Count == 1 && !w.Transfers.Visible[0].IsActive, TimeSpan.FromSeconds(20));
            await Pump(() => w.FileRoots.Any(n => n.Name == "application.yml"), TimeSpan.FromSeconds(10));
            var node = w.FileRoots.First(n => n.Name == "application.yml");
            w.SelectedNode = node;
            var editor = await w.OpenEditorAsync(node);
            editor!.Document.Insert(editor.Document.TextLength, "logging:\n  level: info\n");
            await Pump(() => false, TimeSpan.FromMilliseconds(300));
            Shot(window, "workspace-editor-tab");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AccountsAndTunnelsRender()
    {
        using var h = new StudioHarness();
        var store = new AccountStore(h.Data);
        var accounts = new AccountsViewModel(store, h.Credentials, h.Interaction, () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), startTimer: false, open: (_, _, _) => Task.CompletedTask);
        accounts.AddCommand.Execute(null);
        accounts.EditName = "示例堡垒机";
        accounts.EditUrl = "https://bastion.example";
        accounts.EditTenant = "示例租户";
        accounts.EditUser = "ops";
        accounts.EditPassword = "synthetic";
        accounts.EditSeed = Totp.EncodeBase32(Encoding.ASCII.GetBytes("12345678901234567890"));
        accounts.SaveCommand.Execute(null);
        await Pump(() => accounts.HasSeed, TimeSpan.FromSeconds(3));
        accounts.ToggleRevealCommand.Execute(null);
        var accountsWindow = new AccountsWindow { DataContext = accounts };
        accountsWindow.Show();
        Shot(accountsWindow, "bastion-accounts");
        accountsWindow.Close();

        var via = new StudioProfile { Name = "转发主机", Host = "198.51.100.7", User = "ops" };
        h.Store.Upsert(via);
        h.Store.UpsertTunnel(new TunnelDefinition { Name = "MySQL", ProfileId = via.Id, LocalPort = 13306, RemoteHost = "127.0.0.1", RemotePort = 3306 });
        h.Store.UpsertTunnel(new TunnelDefinition { Name = "Redis", ProfileId = via.Id, LocalPort = 16379, RemoteHost = "10.0.0.12", RemotePort = 6379 });
        h.Store.UpsertTunnel(new TunnelDefinition { Name = "办公网 SOCKS", Kind = TunnelDefinition.Dynamic, ProfileId = via.Id, LocalPort = 11080, RemoteHost = "", RemotePort = 0 });
        Dispatcher.UIThread.RunJobs();
        var window = new MainWindow { Width = 1440, Height = 760, DataContext = h.Model };
        window.Show();
        try
        {
            h.Model.Home.Tabs.Single(t => t.Key == "tunnel").SelectCommand!.Execute(null);
            Shot(window, "library-tunnels");
            h.Model.Home.Tabs[0].SelectCommand!.Execute(null);
            Appearance.Apply(new StudioPreferences { Theme = "明亮" });
            Shot(window, "library-light-with-info-column");
        }
        finally
        {
            Appearance.Apply(new StudioPreferences());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ConnectionEditorAdvancedSectionsRender()
    {
        using var h = new StudioHarness();
        var p = new StudioProfile
        {
            Name = "示例主机", Host = "198.51.100.20", User = "ops", Cipher = "aes256-ctr", KeyExchange = "curve25519-sha256",
            DisableListMonitor = true, SessionEnvironment = [new("LANG", "zh_CN.UTF-8"), new("TZ", "Asia/Shanghai")],
        };
        h.Store.Upsert(p);
        Dispatcher.UIThread.RunJobs();
        var dialog = new Views.Dialogs.ConnectionEditorDialog { DataContext = h.Model.Home.CreateEditor(p) };
        dialog.Show();
        try
        {
            var scroller = dialog.FindControl<Avalonia.Controls.ScrollViewer>("Scroller")!;
            dialog.UpdateLayout();
            scroller.Offset = new Avalonia.Vector(0, dialog.FindControl<Avalonia.Controls.Control>("SectionOther")!.Bounds.Top);
            Shot(dialog, "connection-editor-other");
            scroller.Offset = new Avalonia.Vector(0, 100000);
            Shot(dialog, "connection-editor-environment");
        }
        finally { dialog.Close(); }
    }
}
