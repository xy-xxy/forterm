using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Microsoft.Extensions.Logging;
using Renci.SshNet.Messages.Transport;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>What the independent review of alpha.9 found: linked files used from the session in front, middle clicks on
/// tabs, the transfer clock, a server that says goodbye later, what the program gave up on itself, and what the connection
/// report keeps.</summary>
public sealed class Alpha9ReviewTests
{
    const string Ticket = "0f5a1c22-9b41-4c0e-8d52-1f6a7be30c11";
    static void Settle(Window window) { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }
    static async Task Pump(Func<bool> until, int seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }
    static FileNodeViewModel Row(WorkspaceViewModel w, string name) => w.FileRoots.SelectMany(r => new[] { r }.Concat(r.Children)).First(n => n.Name == name);
    static string Typed(LoopbackFixture f) => string.Concat(f.Stats().GetProperty("input").EnumerateArray().Select(h => Encoding.UTF8.GetString(Convert.FromHexString(h.GetString()!))));
    static async Task<WorkspaceViewModel> Open(StudioHarness h, string entry, string title, int port)
    {
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=" + entry, "-newtab", title, "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{port}"])))!;
        Dispatcher.UIThread.RunJobs();
        return w;
    }
    static Point Middle(Control control, Window window) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    static void MiddleClick(Window window, Point at) { window.MouseDown(at, MouseButton.Middle); window.MouseUp(at, MouseButton.Middle); Settle(window); }

    // ---------- linked files ----------

    /// <summary>A file session that keeps its tab (it has a terminal of its own) and is in front: what is opened from its
    /// files is its own, not that of the hidden session that also shows them.</summary>
    [AvaloniaFact]
    public async Task AFileSessionInFrontKeepsWhatIsOpenedFromItsFiles()
    {
        using var shell = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_DENY_SFTP"] = "1" });
        using var filesServer = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        filesServer.Command("seed-text");
        var terminal = await Open(h, "xshell", "终端会话", shell.Port);
        var files = await Open(h, "xftp", "文件会话", filesServer.Port);
        files.ShowTerminal = true;
        await Pump(() => files.HasShell);
        h.Model.Selected = terminal;
        terminal.Link(files);
        Assert.True(files.IsTabShown);
        Assert.True(files.CanUseTerminalFromFiles);

        h.Model.Selected = files;
        Assert.Same(files, h.Model.Selected);
        await files.ReloadDirectoryAsync(files.CurrentDirectory);
        files.SelectedNode = Row(files, "edit.txt");
        files.OpenEditorCommand.Execute(null);
        await Pump(() => terminal.Editors.Any() || files.Editors.Any());
        var editor = Assert.Single(files.Editors);
        Assert.Empty(terminal.Editors);
        Assert.Same(files, h.Model.Selected);

        files.SelectedNode = Row(files, "a");
        files.CdToTerminalCommand.Execute(null);
        await Pump(() => Typed(filesServer).Contains('\r') || Typed(shell).Length > 0);
        Assert.Equal("cd " + files.SelectedNode!.Path + "\r", Typed(filesServer));
        Assert.Equal("", Typed(shell));
        Assert.Same(files, h.Model.Selected);

        // From the terminal session in front the same file is the draft that is already open: no second one.
        h.Model.Selected = terminal;
        Assert.False(files.IsActive);
        files.SelectedNode = Row(files, "edit.txt");
        files.OpenEditorCommand.Execute(null);
        await Pump(() => ReferenceEquals(h.Model.Selected, files));
        Assert.Empty(terminal.Editors);
        Assert.Same(editor, Assert.Single(files.Editors));
        Assert.Same(editor, files.ActiveTab);
        Assert.Same(files, h.Model.Selected);
    }

    /// <summary>Linked while the terminal session was still connecting: "CD 到终端" is there once it has connected.</summary>
    [AvaloniaFact]
    public async Task ALinkMadeBeforeTheTerminalConnectedOffersCdOnceItHas()
    {
        using var shell = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_DENY_SFTP"] = "1" });
        using var filesServer = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var files = await Open(h, "xftp", "文件会话", filesServer.Port);
        await Pump(() => files.HasFiles);
        var raised = new List<string>();
        files.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        // Not waited for: the terminal session is linked while it connects.
        var launching = h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "终端会话", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{shell.Port}"]));
        var terminal = h.Model.Workspaces.Single(w => w.Title == "终端会话");
        h.Model.Selected = terminal;
        terminal.Link(files);
        Assert.False(terminal.IsConnected);
        Assert.False(files.CanUseTerminalFromFiles);
        raised.Clear();
        await launching;
        await Pump(() => terminal.IsConnected && terminal.HasShell);
        Assert.True(files.CanUseTerminalFromFiles);
        Assert.Contains(nameof(WorkspaceViewModel.CanUseTerminalFromFiles), raised);
        // Which session is in front changes it as well (a file session with a terminal of its own keeps its tab).
        files.ShowTerminal = true;
        await Pump(() => files.HasShell && files.IsTabShown);
        raised.Clear();
        h.Model.Selected = files;
        Assert.True(files.IsActive);
        Assert.Contains(nameof(WorkspaceViewModel.CanUseTerminalFromFiles), raised);
    }

    /// <summary>At the narrowest file column a long linked title gives way; the column's own buttons stay on it.</summary>
    [AvaloniaFact]
    public async Task TheNarrowFileColumnKeepsItsButtons()
    {
        using var shell = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_DENY_SFTP"] = "1" });
        using var filesServer = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = 1300, Height = 760 };
        window.Show();
        try
        {
            var terminal = await Open(h, "xshell", "终端会话", shell.Port);
            var files = await Open(h, "xftp", "很长的文件会话名称一二三四五六", filesServer.Port);
            h.Model.Selected = terminal;
            terminal.Link(files);
            Settle(window);
            var view = window.GetVisualDescendants().OfType<WorkspaceView>().First(v => v.IsEffectivelyVisible);
            typeof(WorkspaceView).GetField("filesWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, 200.0);
            typeof(WorkspaceView).GetMethod("Layout", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!.Invoke(view, null);
            Settle(window);
            // What is hit is what was drawn last.
            window.CaptureRenderedFrame()?.Dispose();
            var pane = view.GetVisualDescendants().OfType<StudioPane>().Single(p => p.Name == "FilesPane");
            Assert.Equal(200, pane.Bounds.Width, 1);
            foreach (var button in pane.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("pane-icon") && b.IsEffectivelyVisible))
            {
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), pane)!.Value;
                Assert.True(right.X <= pane.Bounds.Width + 0.5, $"{button.Name ?? string.Join(".", button.Classes)} ends at {right.X}");
                var hit = window.InputHitTest(Middle(button, window)) as Visual;
                Assert.True(hit != null && (ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button)), $"{button.Name} {Avalonia.Automation.AutomationProperties.GetName(button)} hit {hit?.GetType().Name}");
            }
            // Wide, the whole title.
            typeof(WorkspaceView).GetField("filesWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, 400.0);
            typeof(WorkspaceView).GetMethod("Layout", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!.Invoke(view, null);
            Settle(window);
            Assert.Equal(96, view.FindControl<TextBlock>("FilesTitleText")!.MaxWidth);
        }
        finally { window.Close(); }
    }

    // ---------- close buttons and the middle button ----------

    /// <summary>The middle button closes a session tab; a double click with it closes one, not the next one as well.</summary>
    [AvaloniaFact]
    public async Task AMiddleDoubleClickOnSessionTabsClosesOne()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = 1300, Height = 760 };
        window.Show();
        try
        {
            await Open(h, "xshell", "会话一", server.Port);
            await Open(h, "xshell", "会话二", server.Port);
            await Open(h, "xshell", "会话三", server.Port);
            Settle(window);
            Button Tab(string title) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("session-tab") && (b.DataContext as WorkspaceViewModel)?.Title == title);
            var tab = Tab("会话一");
            var at = tab.TranslatePoint(new Point(12, tab.Bounds.Height / 2), window)!.Value;
            CloseGuard.ForgetForTest();
            window.MouseMove(at);
            MiddleClick(window, at);
            Assert.Equal(["会话二", "会话三"], h.Model.Workspaces.Select(w => w.Title));
            MiddleClick(window, at);
            await Task.Delay(100);
            Settle(window);
            Assert.Equal(["会话二", "会话三"], h.Model.Workspaces.Select(w => w.Title));
            // Meant: after a while it closes again.
            CloseGuard.ForgetForTest();
            MiddleClick(window, at);
            Assert.Equal(["会话三"], h.Model.Workspaces.Select(w => w.Title));
        }
        finally { window.Close(); CloseGuard.ForgetForTest(); }
    }

    /// <summary>The same for the tabs of the centre column.</summary>
    [AvaloniaFact]
    public async Task AMiddleDoubleClickOnTerminalTabsClosesOne()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = 1300, Height = 760 };
        window.Show();
        try
        {
            var w = await Open(h, "xshell", "会话", server.Port);
            await Pump(() => w.HasShell);
            await w.AddTerminalAsync();
            await w.AddTerminalAsync();
            await Pump(() => w.Terminals.Count == 3 && w.Terminals.All(t => t.HasConnection));
            Settle(window);
            Assert.Equal(3, w.Terminals.Count);
            var first = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("chip-tab") && b.IsEffectivelyVisible && ReferenceEquals(b.DataContext, w.Terminals[0]));
            var at = first.TranslatePoint(new Point(10, first.Bounds.Height / 2), window)!.Value;
            CloseGuard.ForgetForTest();
            window.MouseMove(at);
            MiddleClick(window, at);
            await Pump(() => w.Terminals.Count == 2);
            Assert.Equal(2, w.Terminals.Count);
            MiddleClick(window, at);
            await Task.Delay(100);
            Settle(window);
            Assert.Equal(2, w.Terminals.Count);
        }
        finally { window.Close(); CloseGuard.ForgetForTest(); }
    }

    /// <summary>When a × acts and when it is left alone, by the time of the press.</summary>
    [AvaloniaFact]
    public void TheGuardLetsTheUserGoOnAfterAPause()
    {
        try
        {
            CloseGuard.ForgetForTest();
            const long t = 1_000_000;
            Assert.False(CloseGuard.Blocks(1, t));          // closes something
            CloseGuard.Moved(t);
            Assert.True(CloseGuard.Blocks(2, t + 300));     // its double click, and the pause
            Assert.True(CloseGuard.Blocks(3, t + 700));     // still the same burst of clicks
            Assert.False(CloseGuard.Blocks(1, t + 700));    // a single click after the pause
            Assert.True(CloseGuard.Blocks(2, t + 900));     // that one acted: its double click does not
            Assert.False(CloseGuard.Blocks(4, t + 2500));   // clicking on and on stops being "the second" after a while
            // A click that was left alone does not make the next one a second click.
            CloseGuard.ForgetForTest();
            CloseGuard.Moved(t);
            Assert.True(CloseGuard.Blocks(1, t + 100));
            Assert.False(CloseGuard.Blocks(2, t + 600));
        }
        finally { CloseGuard.ForgetForTest(); }
    }

    // ---------- transfers ----------

    /// <summary>A refresh still on its way when the list is disposed does not start its clock again.</summary>
    [AvaloniaFact]
    public async Task TheTransferClockStopsForGoodWithTheList()
    {
        using var queue = new TransferQueue();
        var list = new TransferListViewModel(queue);
        IProgress<TransferProgress>? report = null;
        var ready = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();
        var ticket = queue.Add("big.bin", "合成 · /", async (progress, ct) => { report = progress; ready.Release(); await release.Task; });
        list.Register(ticket.Id, TransferKind.Upload);
        while (!await ready.WaitAsync(10)) Dispatcher.UIThread.RunJobs();
        report!.Report(new TransferProgress(1 << 20, 40 << 20, "传输中"));
        await Pump(() => list.Visible.Any(i => i.IsMoving));
        var clock = (DispatcherTimer)typeof(TransferListViewModel).GetField("clock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(list)!;
        Assert.True(clock.IsEnabled);
        await Task.Delay(150);
        report.Report(new TransferProgress(2 << 20, 40 << 20, "传输中"));
        list.Dispose();
        Dispatcher.UIThread.RunJobs();
        list.Tick();
        Assert.False(clock.IsEnabled);
        release.SetResult();
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.False(clock.IsEnabled);
    }

    /// <summary>While a cancelled transfer cleans up it moves no bytes: no speed, no time left, and no average from it.</summary>
    [AvaloniaFact]
    public async Task ACancelledTransferHasNoSpeed()
    {
        using var queue = new TransferQueue();
        using var list = new TransferListViewModel(queue);
        IProgress<TransferProgress>? report = null;
        var ready = new SemaphoreSlim(0);
        var cleaned = new TaskCompletionSource();
        var ticket = queue.Add("big.bin", "合成 · /", async (progress, ct) =>
        {
            report = progress; ready.Release();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { await cleaned.Task; throw; }
        });
        list.Register(ticket.Id, TransferKind.Upload);
        while (!await ready.WaitAsync(10)) Dispatcher.UIThread.RunJobs();
        report!.Report(new TransferProgress(1 << 20, 40 << 20, "传输中"));
        await Pump(() => list.Visible.Any(i => i.IsMoving));
        await Task.Delay(400);
        report.Report(new TransferProgress(3 << 20, 40 << 20, "传输中"));
        await Pump(() => list.Visible.Single().HasSpeed);
        var item = list.Visible.Single();
        Assert.NotEqual("", item.SpeedText);
        item.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        await Pump(() => !item.IsMoving);
        Assert.False(item.IsMoving);
        Assert.Equal("", item.SpeedText);
        Assert.Equal("", item.RemainingText);
        Assert.Equal("", list.UploadSpeed);
        // Cleaning up may still report; it is no speed. (The queue tells of progress at most ten times a second.)
        await Task.Delay(150);
        report.Report(new TransferProgress(4 << 20, 40 << 20, "传输中"));
        await Pump(() => item.AmountText.StartsWith("4.0 MB", StringComparison.Ordinal));
        Assert.StartsWith("4.0 MB", item.AmountText);
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        list.Tick();
        Assert.False(item.IsMoving);
        Assert.Equal("", item.RemainingText);
        Assert.Equal("", list.UploadSpeed);
        cleaned.SetResult();
        await Pump(() => !item.IsActive);
        Assert.False(item.IsActive);
    }

    // ---------- the server, and the program itself ----------

    /// <summary>A bastion that ends a session (SSH_MSG_DISCONNECT) after it has been in use: said at once, in its words,
    /// not 15 seconds later as a keep-alive that went unanswered.</summary>
    [AvaloniaFact]
    public async Task AServerThatSaysGoodbyeLaterIsToldAtOnceInItsWords()
    {
        using var server = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_TEXT"] = "session closed by admin" });
        using var h = new StudioHarness();
        var w = await Open(h, "xshell", "合成资产", server.Port);
        await Pump(() => w.HasFiles && w.HasShell);
        Assert.True(w.IsConnected);
        var began = DateTime.UtcNow;
        server.Command("bye");
        await Pump(() => !w.IsConnected, 8);
        Assert.False(w.IsConnected);
        Assert.True(DateTime.UtcNow - began < TimeSpan.FromSeconds(5), (DateTime.UtcNow - began).ToString());
        Assert.StartsWith("连接已断开：", w.Status);
        Assert.Contains("服务器断开了连接，代码 11", w.Status);
        Assert.Contains("session closed by admin", w.Status);
        Assert.DoesNotContain("保活", w.Status);
        Assert.True(w.ShowReport);
        Assert.Contains("结果：服务器断开了连接，代码 11", w.Report());
        // Later events change nothing.
        string status = w.Status;
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(status, w.Status);
    }

    /// <summary>The same without the trace (Forterm.Desktop): the transport is left to its keep-alive as before.</summary>
    [Fact]
    public async Task WithoutATraceAGoodbyeIsLeftToTheKeepAlive()
    {
        using var server = LoopbackFixture.Ssh();
        using var session = await SharedSshSession.ConnectAsync("127.0.0.1", server.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), CancellationToken.None, temporary: true,
            options: new SessionConnectOptions { KeepAliveSeconds = 600 });
        using var files = await session.OpenFilesAsync(CancellationToken.None);
        string? fault = null;
        session.Faulted += reason => fault = reason;
        server.Command("bye");
        await Task.Delay(1500);
        Assert.Null(fault);
        Assert.Null(session.CloseReason);
    }

    static async Task<Exception> ConnectTo(int port, SshTrace trace, bool trust = true) =>
        await Assert.ThrowsAnyAsync<Exception>(() => SharedSshSession.ConnectAsync("127.0.0.1", port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(trust), CancellationToken.None,
            temporary: true, options: new SessionConnectOptions { CommonChannels = true, Trace = trace }));
    static (int Port, Task Served) Answering(string answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            try
            {
                using var c = await listener.AcceptTcpClientAsync();
                var stream = c.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
                // The answer is complete; what the client said is read before closing. Closed unread, Windows resets the
                // connection, and the client would rightly say so.
                c.Client.Shutdown(SocketShutdown.Send);
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var buffer = new byte[4096];
                try { while (await stream.ReadAsync(buffer, limit.Token) > 0) { } } catch (Exception) { }
            }
            finally { listener.Stop(); }
        });
        return (port, served);
    }

    /// <summary>What the program gave up on itself is said as such: not blamed on the server, and not in English.</summary>
    [Theory]
    [InlineData("HTTP/1.1 400 Bad Request\r\n\r\n", "对方在说明自己是 SSH 服务器之前就关闭了连接")]
    [InlineData("SSH-1.5-old\r\n", "服务器的 SSH 协议版本不受支持")]
    public async Task NotAnSshServerIsSaidToBeOne(string answer, string said)
    {
        var (port, served) = Answering(answer);
        var trace = new SshTrace();
        var error = await ConnectTo(port, trace);
        await served;
        Assert.False(trace.EndedByPeer);
        Assert.StartsWith(said, SshErrors.Explain(error));
    }

    [Fact]
    public async Task ADeclinedHostKeyIsNoProtocolError()
    {
        using var server = LoopbackFixture.Ssh();
        var trace = new SshTrace();
        var error = await ConnectTo(server.Port, trace, trust: false);
        Assert.False(trace.EndedByPeer);
        Assert.Equal("服务器的主机密钥没有通过核对（指纹没有被接受，或与记录的不一致），没有登录", SshErrors.Explain(error));
        Assert.DoesNotContain("协议错误", SshErrors.Explain(error));
        Assert.Equal(0, server.Stats().GetProperty("auth").GetInt32());
    }

    [AvaloniaFact]
    public async Task TheSessionSaysWhatItGaveUpOn()
    {
        var (port, served) = Answering("HTTP/1.1 400 Bad Request\r\n\r\n");
        using var h = new StudioHarness();
        var w = await Open(h, "xshell", "合成资产", port);
        await Pump(() => w.Status.StartsWith("连接失败", StringComparison.Ordinal));
        await served;
        Assert.Equal("连接失败：对方在说明自己是 SSH 服务器之前就关闭了连接（可能不是 SSH 端口，或被拒绝接入）", w.Status);
        Assert.DoesNotContain("本程序关闭了连接", w.Status);
        Assert.True(w.ShowReport);
    }

    [Fact]
    public void NetworkErrorsAreSaidInWords()
    {
        Assert.Equal("对方拒绝连接（端口没有开放，或被防火墙拒绝）", SshErrors.Explain(new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal("找不到这个主机名", SshErrors.Explain(new SocketException((int)SocketError.HostNotFound)));
        Assert.StartsWith("双方没有共同支持的主机密钥算法", SshErrors.Explain(new Renci.SshNet.Common.SshConnectionException("No matching host key algorithm (server offers ssh-dss)", DisconnectReason.KeyExchangeFailed)));
        Assert.StartsWith("双方没有共同支持的加密算法", SshErrors.Explain(new Renci.SshNet.Common.SshConnectionException("No matching client encryption algorithm (server offers x)", DisconnectReason.KeyExchangeFailed)));
    }

    // ---------- the connection report ----------

    static void Say(SshTrace trace, string template, params (string Key, object? Value)[] values)
    {
        var logger = ((ILoggerFactory)trace).CreateLogger("Renci.SshNet.Session");
        var state = values.Select(v => new KeyValuePair<string, object?>(v.Key, v.Value)).Append(new("{OriginalFormat}", template)).ToArray();
        logger.Log(LogLevel.Information, default, state, null, (_, _) => "");
    }
    static void Message(SshTrace trace, bool sent, string name, int number, string message) =>
        Say(trace, sent ? "[{SessionId}] Sending message {MessageName}({MessageNumber}) to server: '{Message}'." : "[{SessionId}] Received message {MessageName}({MessageNumber}) from server: '{Message}'.",
            ("MessageName", name), ("MessageNumber", (byte)number), ("Message", message));

    /// <summary>The report is pasted into chats: nothing that names a machine, a user or a ticket, wherever the server put it,
    /// and however it was cut.</summary>
    [Fact]
    public void TheReportKeepsNoNamesAddressesOrSecrets()
    {
        var trace = new SshTrace();
        trace.ConcealSecret("Zq7-tok");
        trace.Conceal("jumpbox7");
        Say(trace, "Server version '{ServerIdentification}'.", ("ServerIdentification", "SSH-2.0-OpenSSH_9.6 bastion01 build 7"));
        // Where a refusal is cut, inside the ticket.
        Message(trace, false, "SSH_MSG_CHANNEL_OPEN_FAILURE", 92, "SSH_MSG_CHANNEL_OPEN_FAILURE : #1 2 " + new string('x', 180) + " " + Ticket);
        // Names the server chooses.
        Message(trace, false, "SSH_MSG_CHANNEL_REQUEST", 98, "SSH_MSG_CHANNEL_REQUEST : #0 " + Ticket);
        Message(trace, false, "SSH_MSG_CHANNEL_REQUEST", 98, "SSH_MSG_CHANNEL_REQUEST : #0 keepalive@openssh.com");
        Message(trace, false, "SSH_MSG_USERAUTH_FAILURE", 51, "SSH_MSG_USERAUTH_FAILURE password,10.1.2.3,ops@db1.corp.example (PartialSuccess:False)");
        Message(trace, false, "SSH_MSG_CHANNEL_OPEN_FAILURE", 92, "SSH_MSG_CHANNEL_OPEN_FAILURE : #2 2 user ops: no route to db-prod-01.corp.example [2001:db8::17] via jumpbox7");
        Message(trace, true, "SSH_MSG_CHANNEL_REQUEST", 98, "SSH_MSG_CHANNEL_REQUEST : #5 shell");
        Say(trace, "[{SessionId}] Disconnect received: {ReasonCode} {MessageDescription}.", ("ReasonCode", DisconnectReason.ByApplication),
            ("MessageDescription", "服务器 says bye to ops, token Zq7-tok, key AbCdEfGh1234567890XyZ, at 10:22:33, ask root2@jumpgate"));
        string report = trace.Report(["头"], ["ops", "服务器", "synthetic", Ticket]);

        foreach (string kept in new[] { "0f5a1c22", "9b41", "8d52", "10.1.2.3", "ops@", "db1", "corp.example", "db-prod-01", "2001:db8", "bastion01", "Zq7", "AbCdEfGh1234567890XyZ", "jumpbox7", "root2", "jumpgate" })
            Assert.DoesNotContain(kept, report);
        Assert.DoesNotContain(" ops", report);
        // What is not a name stays.
        Assert.Contains("keepalive@openssh.com", report);
        Assert.Contains("服务器标识：SSH-2.0-OpenSSH_9.6（另有说明，未记录）", report);
        Assert.Contains("← CHANNEL_REQUEST(98) 本方通道0 ‹已隐去›", report);
        Assert.Contains("→ CHANNEL_REQUEST(98) 对方通道5 shell", report);
        Assert.Contains("10:22:33", report);
        // Its own words are not taken apart by a name such as "服务器".
        Assert.Contains("结果：登录完成之前，服务器断开了连接，代码 11/ByApplication（服务器的说明：‹已隐去› says bye", report);
        Assert.True(trace.EndedByPeer);
    }

    /// <summary>A server that repeats the controller's ticket password: not in the report, although it is not kept.</summary>
    [AvaloniaFact]
    public async Task APasswordTheServerRepeatsIsNotInTheReport()
    {
        using var server = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_ODD"] = "refuse", ["FIXTURE_TEXT"] = "ticket synthetic-only refused, ask ops" });
        using var h = new StudioHarness();
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "合成资产", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{server.Port}"])))!;
        await Pump(() => w.ShowReport);
        string report = w.Report();
        Assert.True(report.Contains("refused, ask ops"), report);
        Assert.DoesNotContain("only", report);
        Assert.DoesNotContain("synthetic", report);
    }

    /// <summary>Server text loses every invisible character, and is never cut inside a character.</summary>
    [Fact]
    public void InvisibleCharactersAreDroppedAndNothingIsCutInHalf()
    {
        var trace = new SshTrace();
        Message(trace, false, "SSH_MSG_USERAUTH_BANNER", 53, "SSH_MSG_USERAUTH_BANNER a​b؜c﻿d⁠e­f‮gh");
        Assert.Equal("abcdefgh", trace.Banner);
        var cut = new SshTrace();
        Message(cut, false, "SSH_MSG_USERAUTH_BANNER", 53, "SSH_MSG_USERAUTH_BANNER " + new string('a', 1990) + "。" + new string('b', 7) + "\U0001F600" + "c");
        Assert.True(cut.Banner.Length <= SshTrace.MostBanner);
        Assert.DoesNotContain('\ufffd', cut.Banner);
        for (int i = 0; i < cut.Banner.Length; i++)
            Assert.False(char.IsHighSurrogate(cut.Banner[i]) && (i + 1 == cut.Banner.Length || !char.IsLowSurrogate(cut.Banner[i + 1])), "half a character at " + i);
        Assert.EndsWith("…", cut.Banner);
    }
}
