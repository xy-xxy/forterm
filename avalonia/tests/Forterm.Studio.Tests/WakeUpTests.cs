using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Launch;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>End-to-end wake-up: a controller launch (as forwarded by xshell/xftp) opens the
/// right workspace against an owned loopback server. Software rendering only.</summary>
public sealed class WakeUpTests
{
    static (MainWindow Window, MainWindowViewModel Model, string Data) Open(ScriptedInteraction interaction)
    {
        string data = Paths.TempDirectory();
        var window = new MainWindow { Width = 1440, Height = 860 };
        var model = new MainWindowViewModel(interaction, new KnownHosts(data), new Forterm.Studio.Library.LibraryStore(data), new Forterm.Studio.Library.CredentialStore(new MemoryVault()));
        window.DataContext = model;
        window.Show();
        return (window, model, data);
    }

    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static void Shot(MainWindow window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png"));
    }

    static void Close(MainWindow window, string data)
    {
        window.Close();
        try { Directory.Delete(data, true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task XshellLaunchOpensTerminalFilesAndMonitorOnOneConnection()
    {
        using var fixture = LoopbackFixture.Ssh();
        var interaction = new ScriptedInteraction();
        var (window, model, data) = Open(interaction);
        try
        {
            var plan = LaunchPlan.Parse(["--entry=xshell", "-newtab", "联调-终端", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]);
            var workspace = await window.AcceptLaunchAsync(plan);
            Assert.NotNull(workspace);
            Assert.Equal("联调-终端", workspace.Title);
            Assert.Equal(WorkspaceKind.Terminal, workspace.Kind);
            Assert.True(workspace.IsConnected, workspace.Status);
            Assert.True(workspace.HasShell, workspace.Status);
            Assert.NotEmpty(workspace.FileRoots);
            Assert.True(workspace.ShowFiles && workspace.ShowTerminal && workspace.ShowMonitor);
            Assert.Single(interaction.HostKeys);
            Assert.False(interaction.HostKeys[0].Changed);
            Assert.Empty(interaction.Challenges);
            Assert.Equal("", plan.Connection!.Password);
            Assert.Same(workspace, model.Selected);
            await Pump(() => false, TimeSpan.FromMilliseconds(600));
            Shot(window, "wakeup-xshell-terminal-workspace");
        }
        finally { Close(window, data); }
    }

    [AvaloniaFact]
    public async Task XftpLaunchOpensFileWorkspaceAndTerminalOnDemand()
    {
        using var fixture = LoopbackFixture.Ssh();
        var (window, model, data) = Open(new ScriptedInteraction());
        try
        {
            var workspace = await window.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-url", $"sftp://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));
            Assert.NotNull(workspace);
            Assert.Equal(WorkspaceKind.Sftp, workspace.Kind);
            Assert.True(workspace.IsConnected, workspace.Status);
            Assert.False(workspace.HasShell);
            Assert.False(workspace.ShowTerminal);
            Assert.NotEmpty(workspace.FileRoots);
            Shot(window, "wakeup-xftp-file-workspace");
            // The terminal opens on the same authenticated transport, without a new login.
            workspace.ShowTerminal = true;
            await Pump(() => workspace.HasShell, TimeSpan.FromSeconds(15));
            Assert.True(workspace.HasShell, workspace.Status);
        }
        finally { Close(window, data); }
    }

    [AvaloniaFact]
    public async Task LaunchWithoutPasswordAsksOnceForThisConnectionOnly()
    {
        using var fixture = LoopbackFixture.Ssh();
        var interaction = new ScriptedInteraction(["synthetic-only"]);
        var (window, _, data) = Open(interaction);
        try
        {
            var workspace = await window.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", $"ssh://synthetic@127.0.0.1:{fixture.Port}"]));
            Assert.True(workspace!.IsConnected, workspace.Status);
            var challenge = Assert.Single(interaction.Challenges);
            Assert.True(challenge.Temporary);
        }
        finally { Close(window, data); }
    }

    [AvaloniaFact]
    public async Task RejectedHostKeyLeavesWorkspaceDisconnected()
    {
        using var fixture = LoopbackFixture.Ssh();
        var (window, _, data) = Open(new ScriptedInteraction { TrustHostKeys = false });
        try
        {
            var workspace = await window.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));
            Assert.False(workspace!.IsConnected);
            Assert.False(workspace.HasShell);
            Assert.StartsWith("连接失败", workspace.Status);
        }
        finally { Close(window, data); }
    }

    [AvaloniaFact]
    public async Task XftpFtpLaunchListsTheFtpRoot()
    {
        using var fixture = LoopbackFixture.Ftp();
        var (window, _, data) = Open(new ScriptedInteraction());
        try
        {
            var workspace = await window.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-url", $"ftp://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));
            Assert.Equal(WorkspaceKind.Ftp, workspace!.Kind);
            Assert.True(workspace.IsConnected, workspace.Status);
            Assert.Contains(workspace.FileRoots, n => n.Name == "a" && n.IsDirectory);
            Assert.Contains(workspace.FileRoots, n => n.Name == "edit.txt" && n.IsFile);
            Assert.False(workspace.CanAddTerminal);
            Assert.Empty(workspace.Terminals);
        }
        finally { Close(window, data); }
    }

    [AvaloniaFact]
    public async Task RejectedArgumentsShowNoticeAndOpenNothing()
    {
        var (window, model, data) = Open(new ScriptedInteraction());
        try
        {
            var result = await window.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", "ssh://t:p@h:22", "-bogus"]));
            Assert.Null(result);
            Assert.Empty(model.Workspaces);
            Assert.True(model.HasNotice);
            Assert.Same(model.Home, model.Selected);
            Shot(window, "rejected-arguments-notice");
        }
        finally { Close(window, data); }
    }

    [AvaloniaFact]
    public void EmptyLibraryRendersInBothThemes()
    {
        var (window, _, data) = Open(new ScriptedInteraction());
        try
        {
            Shot(window, "library-empty-dark");
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Shot(window, "library-empty-light");
        }
        finally
        {
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Close(window, data);
        }
    }
}
