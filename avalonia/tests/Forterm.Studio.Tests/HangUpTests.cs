using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Servers (bastions, above all) that let the user log in and then hang up, refuse or say why.</summary>
public sealed class HangUpTests
{
    const string Ticket = "0f5a1c22-9b41-4c0e-8d52-1f6a7be30c11";
    const string ByItself = "没有说明原因就关闭了连接|强行重置|中止";
    static void Settle(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    static async Task Pump(Func<bool> until, int seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }
    static LoopbackFixture Server(string odd, string text = "", string banner = "")
    {
        var environment = new Dictionary<string, string> { ["FIXTURE_ODD"] = odd };
        if (text.Length > 0) environment["FIXTURE_TEXT"] = text;
        if (banner.Length > 0) environment["FIXTURE_BANNER"] = banner;
        return LoopbackFixture.Ssh(environment);
    }
    static LaunchPlan Launch(LoopbackFixture server, string entry = "xshell") =>
        LaunchPlan.Parse(["--entry=" + entry, "-newtab", "合成资产", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{server.Port}"]);
    static async Task<WorkspaceViewModel> Open(StudioHarness h, LoopbackFixture server, string entry = "xshell")
    {
        var workspace = (await h.Model.AcceptLaunchAsync(Launch(server, entry)))!;
        Dispatcher.UIThread.RunJobs();
        return workspace;
    }
    static void NothingForeign(string text)
    {
        Assert.DoesNotContain("disposed", text);
        Assert.DoesNotContain("aborted", text);
        Assert.DoesNotContain("connection", text);
        Assert.DoesNotContain("被拒绝", text);
    }

    [AvaloniaTheory]
    [InlineData("xshell", "close-on-open", "请求打开第一条通道时")]
    [InlineData("xftp", "close-on-open", "请求打开第一条通道时")]
    [InlineData("xshell", "close-on-request", "通道已打开，请求终端或文件服务时")]
    [InlineData("xftp", "close-on-request", "通道已打开，请求终端或文件服务时")]
    [InlineData("xshell", "close-after-login", "登录已通过")]
    [InlineData("xftp", "close-after-login", "登录已通过")]
    public async Task AServerThatHangsUpAfterTheLoginIsSaidToHaveDoneSo(string entry, string odd, string step)
    {
        using var server = Server(odd);
        using var h = new StudioHarness();
        var workspace = await Open(h, server, entry);
        Assert.False(workspace.IsConnected);
        Assert.StartsWith("连接已断开：", workspace.Status);
        Assert.Contains(step, workspace.Status);
        Assert.Matches(ByItself, workspace.Status);
        Assert.Contains("请从控制器重新打开", workspace.Status);
        NothingForeign(workspace.Status);
        NothingForeign(workspace.FileMessage);
        Assert.StartsWith("连接已断开", workspace.FileMessage);
        // The event that says so as well comes later, and changes nothing.
        string status = workspace.Status;
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(status, workspace.Status);
        Assert.False(workspace.HasShell);
        // Once: what is asked of a server that hangs up is not asked again, and nobody logs in again.
        var stats = server.Stats();
        Assert.Equal(1, stats.GetProperty("connections").GetInt32());
        Assert.Equal(1, stats.GetProperty("auth").GetInt32());
        Assert.InRange(stats.GetProperty("opens").GetArrayLength(), 0, 1);
        // Nor is a second channel asked for on a connection that is gone.
        Assert.InRange(workspace.Report().Split("→ CHANNEL_OPEN(90)").Length - 1, 0, 1);
        Assert.Contains("没有连上", h.Model.Access.WakeUps[0].Result);
        Assert.True(workspace.ShowReport);
        Assert.Equal(entry == "xftp", workspace.ShowReportInFiles);
    }

    [AvaloniaFact]
    public async Task WhatTheServerSaysWhenItHangsUpIsShownAndNotCopied()
    {
        string text = "ticket " + Ticket + " of synthetic for 198.51.100.7 is used up\u202e\r\nbye";
        using var server = Server("disconnect-on-open", text, "Welcome " + Ticket + "\r\nCall the operator: 12345");
        using var h = new StudioHarness();
        var workspace = await Open(h, server);
        Assert.Contains("服务器断开了连接，代码 11", workspace.Status);
        Assert.Contains("is used up bye", workspace.Status);
        Assert.DoesNotContain('\u202e', workspace.Status);
        Assert.Equal("Welcome " + Ticket + "\nCall the operator: 12345", workspace.ServerNotice);
        Assert.True(workspace.HasServerNotice);
        workspace.CopyReportCommand.Execute(null);
        await Pump(() => h.Interaction.Clipboard.Length > 0);
        string report = h.Interaction.Clipboard;
        Assert.Contains("连接诊断", report);
        Assert.Contains("is used up", report);
        foreach (string secret in new[] { Ticket, "198.51.100.7", "synthetic", "127.0.0.1", "合成资产", server.Port.ToString(), "operator" }) Assert.DoesNotContain(secret, report);
        Assert.Contains("控制器唤醒，终端会话", report);
        Assert.Contains("服务器标识：SSH-2.0-paramiko", report);
        Assert.Contains("本程序标识：SSH-2.0-Renci", report);
        Assert.Contains("→ USERAUTH_REQUEST(50) password", report);
        Assert.Contains("← USERAUTH_SUCCESS(52)", report);
        Assert.Contains("→ CHANNEL_OPEN(90)", report);
        Assert.Contains("← 登录提示（", report);
        Assert.Contains("代码 11", report);
        Assert.Contains(h.Interaction.Toasts, t => t.Text.Contains("已复制连接诊断"));
    }

    [AvaloniaFact]
    public async Task ARefusedChannelIsAskedForOnceAndTheReasonIsShown()
    {
        using var server = Server("refuse", "target " + Ticket + " is down");
        using var h = new StudioHarness();
        var workspace = await Open(h, server);
        // Still connected: the server said no, twice (terminal, files), and nothing else happened.
        Assert.True(workspace.IsConnected);
        Assert.Equal(2, server.Stats().GetProperty("opens").GetArrayLength());
        Assert.Contains("已登录，但终端和文件都没有打开", workspace.Status);
        Assert.Contains("服务器没能连上目标：target", workspace.Status);
        Assert.Contains("服务器没能连上目标", workspace.FileMessage);
        Assert.True(workspace.ShowReport);
        string report = workspace.Report();
        Assert.Contains("← CHANNEL_OPEN_FAILURE(92) 通道1 代码2 target ‹已隐去› is down", report);
        Assert.Contains("结果：连接仍在", report);
        Assert.DoesNotContain(Ticket, report);
    }

    [AvaloniaFact]
    public async Task AServerThatHangsUpAtTheSecondRequestAnswersTheFirst()
    {
        // What a bastion may do to a client that asks again at once, as the library does by itself.
        using var server = Server("refuse-then-close", "not allowed");
        using (var h = new StudioHarness())
        {
            var workspace = await Open(h, server, "xftp");
            Assert.True(workspace.IsConnected);
            Assert.Equal(1, server.Stats().GetProperty("opens").GetArrayLength());
            Assert.Contains("已登录，但文件没有打开", workspace.Status);
            Assert.Contains("服务器没能连上目标：not allowed", workspace.FileMessage);
            Assert.True(workspace.ShowReportInFiles);
        }
        using var old = Server("refuse-then-close", "not allowed");
        using var session = await SharedSshSession.ConnectAsync("127.0.0.1", old.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), CancellationToken.None, temporary: true);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => session.OpenFilesAsync(CancellationToken.None));
        Assert.DoesNotContain("not allowed", error.Message);
        Assert.Equal(2, old.Stats().GetProperty("opens").GetArrayLength());
    }

    [AvaloniaFact]
    public async Task ChannelsAreAskedForAsCommonClientsDo()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var workspace = await Open(h, server);
        Assert.Equal("已连接：终端与文件复用同一 SSH 连接", workspace.Status);
        Assert.False(workspace.ShowReport);
        Assert.True(workspace.CanReport);
        var stats = server.Stats();
        Assert.Equal([32768, 32768], stats.GetProperty("opens").EnumerateArray().Select(o => o.GetProperty("packet").GetInt32()));
        // ISPEED and OSPEED, 38400 each, and the end of the list.
        Assert.Equal("800000960081000096" + "0000", Assert.Single(stats.GetProperty("ptyModes").EnumerateArray()).GetString());
        Assert.Contains("结果：连接仍在", workspace.Report());
        // As the frozen program asks, and it still does.
        using var old = LoopbackFixture.Ssh();
        using var session = await SharedSshSession.ConnectAsync("127.0.0.1", old.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), CancellationToken.None);
        using var shell = await session.OpenShellAsync(80, 24, CancellationToken.None);
        Assert.Equal([65536], old.Stats().GetProperty("opens").EnumerateArray().Select(o => o.GetProperty("packet").GetInt32()));
        Assert.Equal("", Assert.Single(old.Stats().GetProperty("ptyModes").EnumerateArray()).GetString());
        using var held = await session.OpenShellAsync(80, 24, CancellationToken.None, "xterm", [], flowControl: true);
        Assert.Equal(["", ""], old.Stats().GetProperty("ptyModes").EnumerateArray().Select(m => m.GetString()!));
    }

    [AvaloniaFact]
    public async Task ATerminalThatIsNotAllowedSaysWhyAndTheFilesStay()
    {
        using var server = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_DENY_SHELL"] = "1" });
        using var h = new StudioHarness();
        var workspace = await Open(h, server);
        Assert.True(workspace.IsConnected);
        Assert.True(workspace.HasFiles);
        Assert.Equal("已连接：文件可用，终端没有打开：服务器不允许启动终端", workspace.Status);
        // Only one of the two: the session works, and the report is in the palette.
        Assert.False(workspace.ShowReport);
        Assert.True(workspace.CanReport);
    }

    [AvaloniaFact]
    public async Task ASessionThatEndsUnseenIsToldOnce()
    {
        using var server = Server("close-on-open");
        using var h = new StudioHarness();
        var opening = h.Model.AcceptLaunchAsync(Launch(server));
        h.Model.ShowHomeCommand.Execute(null);
        var workspace = (await opening)!;
        Assert.False(workspace.IsActive);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(h.Interaction.Toasts, t => t.Text.Contains("的连接已断开"));
    }

    [AvaloniaFact]
    public async Task ThereIsNoReportWithoutSsh()
    {
        using var server = LoopbackFixture.Ftp();
        using var h = new StudioHarness();
        var workspace = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-url", $"ftp://synthetic:synthetic-only@127.0.0.1:{server.Port}"])))!;
        Assert.True(workspace.IsConnected);
        Assert.False(workspace.CanReport);
        h.Model.Selected = workspace;
        h.Model.OpenPaletteCommand.Execute(null);
        Assert.DoesNotContain(h.Model.Palette.Matches, e => e.Title == "复制连接诊断");
        workspace.CopyReportCommand.Execute(null);
        Assert.Equal("", h.Interaction.Clipboard);
    }

    [AvaloniaFact]
    public async Task ASavedConnectionThatIsHungUpOnSaysSoAndHidesItsOwnNames()
    {
        using var server = Server("disconnect-on-open", "synthetic@127.0.0.1 not here");
        using var h = new StudioHarness();
        var profile = h.Ssh(server.Port, "合成机器");
        var workspace = await h.Model.OpenProfileAsync(profile);
        Assert.False(workspace.IsConnected);
        Assert.Contains("不会自动重连，请重新发起连接", workspace.Status);
        Assert.Contains("已保存连接，终端会话", workspace.Report());
        Assert.DoesNotContain("synthetic", workspace.Report());
        Assert.DoesNotContain("127.0.0.1", workspace.Report());
    }

    [AvaloniaFact]
    public async Task ASessionThatEndsLaterIsExplainedToo()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var workspace = await Open(h, server);
        await Pump(() => workspace.HasFiles && workspace.HasShell);
        server.Command("drop");
        await Pump(() => !workspace.IsConnected);
        Assert.StartsWith("连接已断开：", workspace.Status);
        Assert.Matches(ByItself, workspace.Status);
        // The files that were listed stay where they are.
        Assert.Equal("", workspace.FileMessage);
        Assert.True(workspace.ShowReport);
        // The terminal has something to show: there is no placeholder in front of it.
        Assert.True(workspace.ShowReportInFiles);
        string status = workspace.Status;
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(status, workspace.Status);
    }

    [AvaloniaFact]
    public async Task ClosingASessionIsNotSaidToBeTheServersDoing()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var workspace = await Open(h, server);
        string status = workspace.Status;
        workspace.CloseCommand!.Execute(null);
        await Pump(() => server.Stats().GetProperty("activeTransports").GetInt32() == 0);
        Assert.Equal(status, workspace.Status);
        Assert.Contains("本程序关闭了连接", workspace.Report());
    }

    [AvaloniaFact]
    public async Task ThePaletteOffersTheReportOfTheSessionShown()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        h.Model.OpenPaletteCommand.Execute(null);
        Assert.DoesNotContain(h.Model.Palette.Matches, e => e.Title == "复制连接诊断");
        h.Model.Palette.CloseCommand.Execute(null);
        var workspace = await Open(h, server);
        h.Model.Selected = workspace;
        h.Model.OpenPaletteCommand.Execute(null);
        h.Model.Palette.Text = "诊断";
        var entry = Assert.Single(h.Model.Palette.Matches, e => e.Title == "复制连接诊断");
        h.Model.Palette.RunCommand.Execute(entry);
        await Pump(() => h.Interaction.Clipboard.Length > 0);
        Assert.Contains("→ CHANNEL_REQUEST(98) 通道0 pty-req", h.Interaction.Clipboard);
        Assert.Contains("→ CHANNEL_REQUEST(98) 通道0 shell", h.Interaction.Clipboard);
        Assert.Contains("→ CHANNEL_REQUEST(98) 通道1 subsystem", h.Interaction.Clipboard);
    }

    [AvaloniaTheory]
    [InlineData("xshell", 1100, 700, "terminal")]
    [InlineData("xftp", 1100, 700, "files")]
    [InlineData("xftp", 640, 420, "files-small")]
    public async Task TheReasonAndTheButtonAreOnTheScreen(string entry, int width, int height, string name)
    {
        using var server = Server("disconnect-on-open", "The ticket was already used.", "堡垒机登录提示（合成）\n第二行");
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = width, Height = height };
        window.Show();
        try
        {
            var workspace = await Open(h, server, entry);
            h.Model.Selected = workspace;
            Settle(window);
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content as string == "复制连接诊断").ToArray();
            var button = Assert.Single(buttons);
            var place = button.TranslatePoint(default, window)!.Value;
            Assert.InRange(place.X, 0, width - button.Bounds.Width);
            Assert.InRange(place.Y, 0, height - button.Bounds.Height);
            Assert.Contains(window.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.IsEffectivelyVisible && t.Text!.Contains("第二行"));
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusText" && t.IsEffectivelyVisible);
            Assert.InRange(status.TranslatePoint(new Point(status.Bounds.Width, 0), window)!.Value.X, 1, window.Bounds.Width);
            Assert.Equal(workspace.Status, ToolTip.GetTip(status));
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "hang-up-" + name + ".png"));
        }
        finally { window.Close(); }
    }
}

/// <summary>The trace itself: what it keeps, and what it must not.</summary>
public sealed class SshTraceTests
{
    static void Say(SshTrace trace, string template, params (string Key, object? Value)[] values) => Say(trace, null, template, values);
    static void Say(SshTrace trace, Exception? error, string template, params (string Key, object? Value)[] values)
    {
        var logger = ((Microsoft.Extensions.Logging.ILoggerFactory)trace).CreateLogger("Renci.SshNet.Session");
        var state = values.Select(v => new KeyValuePair<string, object?>(v.Key, v.Value)).Append(new("{OriginalFormat}", template)).ToArray();
        logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, default, state, error, (_, _) => "");
    }
    static void Message(SshTrace trace, bool sent, string name, int number, string? message = null) =>
        Say(trace, "[{SessionId}] " + (sent ? "Sending" : "Received") + " message {MessageName}({MessageNumber})", ("MessageName", name), ("MessageNumber", (byte)number), ("Message", message ?? name));
    static string Report(SshTrace trace, params string[] hide) => trace.Report([], hide);

    [Fact]
    public void OnlyNamesAreKeptOfWhatIsSaid()
    {
        var trace = new SshTrace();
        Say(trace, "Initiating connection to '{EndPoint}'.", ("EndPoint", "198.51.100.7:2222"));
        Say(trace, "[{SessionId}] Host key algorithm: we offer {WeOffer}", ("WeOffer", "secret-offer"));
        Message(trace, true, "SSH_MSG_USERAUTH_REQUEST", 50, "SSH_MSG_USERAUTH_REQUEST (password)");
        Message(trace, true, "SSH_MSG_USERAUTH_REQUEST", 50, "SSH_MSG_USERAUTH_REQUEST (hunter2 hunter2)");
        Message(trace, false, "SSH_MSG_CHANNEL_DATA", 94, "SSH_MSG_CHANNEL_DATA : #0 hunter2\nhunter2");
        Message(trace, false, "SSH_MSG_CHANNEL_REQUEST", 98, "SSH_MSG_CHANNEL_REQUEST : #0 exit status hunter2");
        Message(trace, false, "SSH_MSG_USERAUTH_FAILURE", 51, "SSH_MSG_USERAUTH_FAILURE password,hunter2 ! (PartialSuccess:False)");
        string report = Report(trace);
        foreach (string no in new[] { "198.51", "secret", "hunter2" }) Assert.DoesNotContain(no, report);
        Assert.Contains("USERAUTH_REQUEST(50) password", report);
    }

    [Fact]
    public void ItIsBoundedAndStillKnowsTheEnd()
    {
        var trace = new SshTrace();
        var logger = ((Microsoft.Extensions.Logging.ILoggerFactory)trace).CreateLogger("Renci.SshNet.Session");
        for (int i = 0; i < 5000; i++) Message(trace, false, "SSH_MSG_CHANNEL_DATA", 94, "SSH_MSG_CHANNEL_DATA : #0");
        // Data is one line as long as nothing else is between, whatever the channel and the direction.
        for (int i = 0; i < 5000; i++) Message(trace, i % 2 == 0, i % 3 == 0 ? "SSH_MSG_CHANNEL_WINDOW_ADJUST" : "SSH_MSG_CHANNEL_DATA", 94, "SSH_MSG_CHANNEL_DATA : #" + i);
        Assert.Contains("↔ 通道数据 ×10000", Report(trace));
        for (int i = 0; i < 5000; i++) Message(trace, i % 2 == 0, "SSH_MSG_CHANNEL_CLOSE", 97, "SSH_MSG_CHANNEL_CLOSE : #" + i);
        Assert.True(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Trace));
        Assert.True(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information));
        Assert.False(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug));
        Message(trace, false, "SSH_MSG_USERAUTH_BANNER", 53, "SSH_MSG_USERAUTH_BANNER " + new string('字', 9000));
        Assert.Equal(SshTrace.MostBanner, trace.Banner.Length);
        Say(trace, new Renci.SshNet.Common.SshConnectionException("x", Renci.SshNet.Messages.Transport.DisconnectReason.ConnectionLost, new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset)), "[{SessionId}] Raised exception");
        Assert.Contains("强行重置", trace.Explanation);
        Say(trace, "[{SessionId}] Disconnecting session.");
        Assert.Contains("强行重置", trace.Explanation);
        string report = Report(trace);
        Assert.InRange(report.Split('\n').Length, 100, SshTrace.MostFirst + SshTrace.MostLast + 20);
        // How it began, what came last, and how much is missing between them.
        Assert.Contains("CHANNEL_CLOSE(97) 通道0", report);
        Assert.Contains("CHANNEL_CLOSE(97) 通道4999", report);
        Assert.DoesNotContain("通道2500", report);
        Assert.Contains("中间省略 " + (5000 + 1 + 1 - SshTrace.MostFirst - SshTrace.MostLast) + " 行", report);
        Assert.EndsWith("没有包含：地址、用户名、密码、票据、终端内容、文件名。", report);
    }

    [Theory]
    [InlineData("", "登录完成之前，")]
    [InlineData("52", "登录已通过，还没有请求打开通道，")]
    [InlineData("52 90", "登录已通过，请求打开第一条通道时，")]
    [InlineData("52 90 92", "登录已通过，服务器拒绝打开通道后，")]
    [InlineData("52 90 91", "登录已通过，通道已打开，请求终端或文件服务时，")]
    [InlineData("52 90 91 99", "登录已通过，终端或文件服务刚打开时，")]
    [InlineData("52 90 91 99 94", "")]
    public void TheStepIsTold(string messages, string step)
    {
        var names = new Dictionary<string, (string Name, bool Sent)> { ["52"] = ("SSH_MSG_USERAUTH_SUCCESS", false), ["90"] = ("SSH_MSG_CHANNEL_OPEN", true), ["91"] = ("SSH_MSG_CHANNEL_OPEN_CONFIRMATION", false), ["92"] = ("SSH_MSG_CHANNEL_OPEN_FAILURE", false), ["99"] = ("SSH_MSG_CHANNEL_SUCCESS", false), ["94"] = ("SSH_MSG_CHANNEL_DATA", false) };
        var trace = new SshTrace();
        Assert.Equal("", trace.Explanation);
        foreach (string number in messages.Split(' ', StringSplitOptions.RemoveEmptyEntries)) Message(trace, names[number].Sent, names[number].Name, int.Parse(number));
        // Nothing the other side does makes these up.
        Message(trace, true, "SSH_MSG_USERAUTH_SUCCESS", 52); Message(trace, false, "SSH_MSG_CHANNEL_OPEN", 90);
        Say(trace, new Renci.SshNet.Common.SshConnectionException("An established connection was aborted by the server.", Renci.SshNet.Messages.Transport.DisconnectReason.ConnectionLost), "[{SessionId}] Raised exception");
        Assert.Equal(step + "服务器没有说明原因就关闭了连接", trace.Explanation);
        Assert.True(trace.IsOver);
        Assert.Equal(messages.Length > 0, trace.LoggedIn);
    }

    [Theory]
    [InlineData("from 198.51.100.40 to 203.0.113.9:22", "from ‹已隐去› to ‹已隐去›:22")]
    [InlineData("0f5a1c22-9b41-4c0e-8d52-1f6a7be30c11 and 0F5A1C229B414C0E8D521F6A7BE30C11", "‹已隐去› and ‹已隐去›")]
    [InlineData("user Operator9 at bastion.example", "user ‹已隐去› at ‹已隐去›")]
    [InlineData("OpenSSH_9.6 version 1.2.3", "OpenSSH_9.6 version 1.2.3")]
    public void WhatAServerRepeatsIsHidden(string said, string shown) => Assert.Equal(shown, SshTrace.Hide(said, ["operator9", "bastion.example", "", "abc"]));
}
