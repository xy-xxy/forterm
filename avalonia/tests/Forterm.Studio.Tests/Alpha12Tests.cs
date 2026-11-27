using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.12: what a real bastion showed on 11-27. "读取监控" opened a command channel, the bastion accepted it
/// and 0.1 s later ended the whole connection, terminal and all.</summary>
public sealed class Alpha12Tests
{
    static async Task Pump(Func<bool> until, int seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }
    static async Task<WorkspaceViewModel> Launch(StudioHarness h, LoopbackFixture fixture, string title = "唤醒")
    {
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", title, "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!;
        await Pump(() => w.Terminal is { IsLive: true });
        return w;
    }
    static int Execs(LoopbackFixture f) => f.Stats().GetProperty("execs").GetInt32();

    /// <summary>A controller-launched session: the first command channel is asked about, and not opened when refused;
    /// once allowed, not asked again in the session.</summary>
    [AvaloniaFact]
    public async Task ALaunchedSessionAsksBeforeTheFirstCommand()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            h.Interaction.ConfirmAnswer = false;
            int asked = h.Interaction.Confirms.Count;
            Assert.False(await w.Monitor!.RefreshAsync());
            Assert.Equal(asked + 1, h.Interaction.Confirms.Count);
            Assert.StartsWith("在这个连接上执行命令：读取监控会在这个连接上另开一个命令通道", h.Interaction.Confirms[^1]);
            Assert.Contains("会直接断开整个连接（终端也会断开）", h.Interaction.Confirms[^1]);
            Assert.Equal(0, Execs(fixture));
            Assert.Equal("没有读取监控", w.Monitor.Message);
            // Refreshing by itself never asks, and reads nothing.
            Assert.False(await w.Monitor.RefreshAsync(byUser: false));
            Assert.Equal(asked + 1, h.Interaction.Confirms.Count);
            Assert.Equal(0, Execs(fixture));

            h.Interaction.ConfirmAnswer = true;
            Assert.True(await w.Monitor.RefreshAsync());
            Assert.Equal(1, Execs(fixture));
            Assert.True(await w.Monitor.RefreshAsync());
            Assert.True(await w.Monitor.RefreshAsync(byUser: false));
            Assert.Equal(asked + 2, h.Interaction.Confirms.Count);
            Assert.Equal(3, Execs(fixture));
            Assert.True(w.IsConnected);
        }
        finally { window.Close(); }
    }

    /// <summary>The bastion of 11-27: the command is accepted and the connection ends at once. The status and the
    /// connection report say it came right after a command; the server is remembered, and the next session asks again
    /// with that said, and does not read by itself.</summary>
    [AvaloniaFact]
    public async Task AServerThatEndsTheConnectionAfterACommandIsRemembered()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_EXEC_HANGUP"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Assert.False(await w.Monitor!.RefreshAsync());
            await Pump(() => !w.IsConnected, 15);
            Assert.False(w.IsConnected);
            Assert.Contains("本程序在这个连接上执行命令（读取监控、压缩、sudo 等）", w.Status);
            w.CopyReportCommand.Execute(null);
            await Pump(() => h.Interaction.Clipboard.Length > 0);
            Assert.Contains("结果：本程序在这个连接上执行命令（读取监控、压缩、sudo 等）0.", h.Interaction.Clipboard);
            Assert.True(File.Exists(Path.Combine(h.Data, "command-channel-ends.json")));
            Assert.DoesNotContain("127.0.0.1", File.ReadAllText(Path.Combine(h.Data, "command-channel-ends.json")));

            // The next session to the same server: nothing by itself, and asked with what happened.
            var next = await Launch(h, fixture, "再次唤醒");
            int execs = Execs(fixture), asked = h.Interaction.Confirms.Count;
            Assert.False(await next.Monitor!.RefreshAsync(byUser: false));
            Assert.Equal(asked, h.Interaction.Confirms.Count);
            Assert.Equal(execs, Execs(fixture));
            Assert.Contains("上次在执行命令后断开了连接", next.Monitor.Message);
            h.Interaction.ConfirmAnswer = false;
            Assert.False(await next.Monitor.RefreshAsync());
            Assert.StartsWith("这台服务器上次执行命令后断开了连接：", h.Interaction.Confirms[^1]);
            Assert.True(next.IsConnected);
        }
        finally { window.Close(); }
    }

    /// <summary>A saved connection to an ordinary server: nothing is asked, as before.</summary>
    [AvaloniaFact]
    public async Task ASavedConnectionIsNotAsked()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port));
        try
        {
            await Pump(() => w.IsConnected, 15);
            int asked = h.Interaction.Confirms.Count, execs = Execs(fixture); // a saved connection may refresh by itself
            Assert.True(await w.Monitor!.RefreshAsync());
            Assert.Equal(asked, h.Interaction.Confirms.Count);
            Assert.True(Execs(fixture) > execs);
        }
        finally { h.Model.Close(w); }
    }

    /// <summary>What is kept names no server: a keyed hash of the address, and nothing else.</summary>
    [Fact]
    public void TheRememberedServerIsAHash()
    {
        string key = CommandChannels.Key(" Bastion.Example ", 22);
        Assert.Equal(64, key.Length);
        Assert.Equal(key, CommandChannels.Key("bastion.example", 22));
        Assert.NotEqual(key, CommandChannels.Key("bastion.example", 2222));
    }

    /// <summary>The connection report says how the controller filled user and password, never what with: a bastion's own
    /// client builds the address, and its template is to be learned from it.</summary>
    [Fact]
    public void TheLaunchIsDescribedNotQuoted()
    {
        string sid = "0f8fad5b-d9cb-469f-a165-70867728950e";
        Assert.Equal("用户名 36 字（标识）；密码与用户名相同", WorkspaceViewModel.LaunchShape(sid, sid, true));
        Assert.Equal("用户名 36 字（标识）；密码 8 字，与用户名不同", WorkspaceViewModel.LaunchShape(sid, "a1b2c3d4", true));
        Assert.Equal("用户名 36 字（标识）；没有密码", WorkspaceViewModel.LaunchShape(sid, "", false));
        Assert.Equal("用户名 36 字（标识）；密码为空", WorkspaceViewModel.LaunchShape(sid, "", true));
        Assert.Equal("用户名 4 字（字母和数字）；密码 8 字，包含用户名", WorkspaceViewModel.LaunchShape("root", "root-x!y", true));
        Assert.DoesNotContain("a1b2", WorkspaceViewModel.LaunchShape(sid, "a1b2c3d4", true));
    }

    /// <summary>In the report of a controller launch, after the way it was opened.</summary>
    [AvaloniaFact]
    public async Task TheReportCarriesTheLaunchShape()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            string report = w.Report();
            Assert.Contains("唤醒参数：用户名 9 字（字母和数字）；密码 14 字，包含用户名", report);
            Assert.DoesNotContain("synthetic-only", report);
        }
        finally { window.Close(); }
    }
}
