using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;

namespace Forterm.Core;

/// <summary>Owns a single authenticated SSH transport, never reconnects. All
/// views/channels have leases so closing one page cannot close another page.</summary>
public sealed partial class SharedSshSession : IDisposable
{
    readonly SshClient client; readonly IDisposable? upstream; readonly IDisposable? upstreamLease; readonly SessionConnectOptions options; readonly CancellationTokenSource transportClosed = new();
    string? failure; public string? CloseReason => Volatile.Read(ref failure); public event Action<string>? Faulted;
    readonly object gate = new();
    readonly SemaphoreSlim monitorOperation = new(1, 1);
    int references = 1;
    bool closing; int transportDisposed;
    public bool IsConnected => Volatile.Read(ref transportDisposed) == 0 && client.IsConnected;
    public static async Task<SharedSshSession> ConnectAsync(string host, int port, string username,
        string password, Func<string, CancellationToken, Task<bool>> confirmFingerprint, CancellationToken ct, bool temporary = false, Func<CredentialChallenge, CancellationToken, Task<string[]?>>? challenge = null, string authMode = "password", string keyPath = "", string keyPassphrase = "", SessionConnectOptions? options = null)
    {
        options ??= new SessionConnectOptions(); options.Validate();
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(45, options.ConnectTimeoutSeconds))); var authToken = timeout.Token;
        using var authentication = await Task.Run(() => new SessionAuthentication(username, password, temporary, challenge, authToken, authMode, keyPath, keyPassphrase, options), CancellationToken.None).ConfigureAwait(false); authToken.ThrowIfCancellationRequested();
        IDisposable? jumpLease = null; JumpProxy? relay = null;
        if (options.Jump != null) { jumpLease = options.Jump.Retain(); try { relay = new JumpProxy(options.Jump.client, host, port); } catch { jumpLease.Dispose(); throw; } }
        var info = relay != null ? new ConnectionInfo(host, port, username, ProxyTypes.Socks5, "127.0.0.1", relay.Port, relay.Username, relay.Password, authentication.Methods.ToArray())
            : options.Proxy is { } proxy ? new ConnectionInfo(host, port, username, proxy.Kind == ProxyKind.Http ? ProxyTypes.Http : ProxyTypes.Socks5, proxy.Host, proxy.Port, proxy.User, proxy.Password, authentication.Methods.ToArray())
            : new ConnectionInfo(host, port, username, authentication.Methods.ToArray());
        info.Timeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds); info.MaxSessions = options.MaxChannels;
        SshAlgorithms.Apply(info, options);
        var client = new SshClient(info) { KeepAliveInterval = Timeout.InfiniteTimeSpan };
        string? trustedFingerprint = null;
        client.HostKeyReceived += (_, e) =>
        {
            var fp = "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
            if (trustedFingerprint != null) { e.CanTrust = StringComparer.Ordinal.Equals(trustedFingerprint, fp); return; }
            e.CanTrust = confirmFingerprint(fp, authToken).GetAwaiter().GetResult();
            if (e.CanTrust) trustedFingerprint = fp;
        };
        try { await client.ConnectAsync(authToken).ConfigureAwait(false); return new(client, options, relay, jumpLease); }
        catch (Exception error) { client.Dispose(); relay?.Dispose(); jumpLease?.Dispose(); if (relay?.Failure != null) throw new IOException("跳板转发未成功；原跳板会话保持：" + relay.Failure, error); throw; }
        finally { timeout.Cancel(); }
    }
    SharedSshSession(SshClient client, SessionConnectOptions options, IDisposable? upstream = null, IDisposable? upstreamLease = null) { this.client = client; this.options = options; this.upstream = upstream; this.upstreamLease = upstreamLease; client.ErrorOccurred += (_, _) => ThreadPool.QueueUserWorkItem(_ => FailTransport("SSH连接已中断；已停止此连接，不自动重用凭据。")); if (options.KeepAliveEnabled) _ = KeepAlive(); }
    async Task KeepAlive()
    {
        try { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.KeepAliveSeconds)); while (await timer.WaitForNextTickAsync(transportClosed.Token)) { await client.ProbeTransportAsync(TimeSpan.FromSeconds(options.KeepAliveSeconds), 3, transportClosed.Token); } }
        catch (OperationCanceledException) when (transportClosed.IsCancellationRequested) { }
        catch (Exception) { if (!transportClosed.IsCancellationRequested) FailTransport("SSH协议保活连续三个应答窗口无响应或连接不可用；已停止此连接，不自动重用凭据。"); }
    }
    void FailTransport(string reason) { if (Volatile.Read(ref transportDisposed) != 0) return; if (Interlocked.CompareExchange(ref failure, reason, null) == null) { try { Faulted?.Invoke(reason); } finally { StopTransport(); } } }
    void StopTransport() { if (Interlocked.Exchange(ref transportDisposed, 1) != 0) return; try { transportClosed.Cancel(); } catch { } try { client.Dispose(); } catch { } finally { try { upstream?.Dispose(); } finally { upstreamLease?.Dispose(); } } }
    public IDisposable Retain()
    {
        lock (gate) { ObjectDisposedException.ThrowIf(closing || !IsConnected, this); references++; return new Lease(this); }
    }
    sealed class Lease(SharedSshSession owner) : IDisposable
    {
        SharedSshSession? session = owner;
        public void Dispose() => Interlocked.Exchange(ref session, null)?.Release();
    }
    /// <summary>The algorithms this transport negotiated (empty strings before key exchange). The key
    /// exchange is the implementation's name: aliases such as curve25519-sha256@libssh.org report as
    /// curve25519-sha256.</summary>
    public SshNegotiated Negotiated
    {
        get { var info = client.ConnectionInfo; return new(info.CurrentKeyExchangeAlgorithm ?? "", info.CurrentHostKeyAlgorithm ?? "", info.CurrentClientEncryption ?? ""); }
    }
    /// <summary>Opens a shell whose channel first sends the given "env" requests. Variables the
    /// server refuses are listed in <see cref="SshPtyConnection.RejectedEnvironment"/>; the shell
    /// still opens. Nothing is written to the terminal. With <paramref name="flowControl"/> a server
    /// that sends faster than the terminal reads is slowed down (as OpenSSH does); without it the
    /// terminal channel is closed when too much is waiting.</summary>
    public async Task<SshPtyConnection> OpenShellAsync(int cols, int rows, CancellationToken ct, string terminalType, IReadOnlyList<KeyValuePair<string, string>> environment, bool flowControl = false)
    {
        if (environment.Count == 0 && !flowControl) return await OpenShellAsync(cols, rows, ct, terminalType).ConfigureAwait(false);
        ShellEnvironment.Validate(environment);
        string term = ValidTerminalType(terminalType);
        var variables = environment.ToArray();
        var lease = Retain(); ShellStream? shell = null;
        try
        {
            IReadOnlyList<string> rejected = Array.Empty<string>();
            shell = await Task.Run(() => { var created = client.CreateShellStream(term, (uint)cols, (uint)rows, 0, 0, 65536, variables, out var refused, flowControl ? SshPtyConnection.FlowWindow : 0); rejected = refused; return created; }, CancellationToken.None).ConfigureAwait(false);
            if (ct.IsCancellationRequested) { shell.Dispose(); ct.ThrowIfCancellationRequested(); }
            return new SshPtyConnection(shell, lease, flowControl) { RejectedEnvironment = rejected };
        }
        catch { shell?.Dispose(); lease.Dispose(); throw; }
    }
    public async Task<SshPtyConnection> OpenShellAsync(int cols, int rows, CancellationToken ct, string terminalType = "xterm-256color")
    {
        var lease = Retain(); ShellStream? shell = null;
        try
        {
            // Requesting a shell may block until server response; channel open
            // is bounded by ConnectionInfo.Timeout. A late result is disposed.
            shell = await Task.Run(() => client.CreateShellStream(ValidTerminalType(terminalType), (uint)cols, (uint)rows, 0, 0, 65536), CancellationToken.None).ConfigureAwait(false);
            if (ct.IsCancellationRequested) { shell.Dispose(); ct.ThrowIfCancellationRequested(); }
            return new SshPtyConnection(shell, lease);
        }
        catch { shell?.Dispose(); lease.Dispose(); throw; }
    }
    /// <summary>TERM sent in pty-req: letters, digits and "-._+" only, at most 64 characters.</summary>
    public static string ValidTerminalType(string? value)
    {
        value = value?.Trim() ?? "";
        if (value.Length == 0) return "xterm-256color";
        if (value.Length > 64 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '+')) throw new ArgumentException("终端类型只允许字母、数字和 -._+，最长64个字符");
        return value;
    }
    public async Task<SftpLease> OpenFilesAsync(CancellationToken ct)
    {
        var lease = Retain();
        try
        {
            var files = await Task.Run(client.OpenSharedSftpClient, CancellationToken.None).ConfigureAwait(false);
            if (ct.IsCancellationRequested) { files.Dispose(); ct.ThrowIfCancellationRequested(); }
            return new(files, lease, options, transportClosed.Token, this);
        }
        catch { lease.Dispose(); throw; }
    }
    /// <summary>Fixed read-only Linux probe on its own exec channel. Never writes to a PTY.
    /// A timed-out open may finish later; the single-flight gate stays held until cleanup.</summary>
    public async Task<LinuxSample> ReadLinuxMetricsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await monitorOperation.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("上次监控请求尚在收尾，请稍后刷新");
        IDisposable retained;
        try { retained = Retain(); } catch { monitorOperation.Release(); throw; }
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, transportClosed.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var requestToken = timeout.Token;
        var worker = Task.Run(async () =>
        {
            try
            {
                using var command = client.CreateCommand(LinuxMetrics.Command);
                command.MaxOutputBytes = 1024 * 1024;
                command.CommandTimeout = TimeSpan.FromSeconds(8);
                await command.ExecuteAsync(timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (command.ExitStatus != 0) throw new IOException("监控命令未正常退出；服务器可能不支持 Linux /proc 或禁止所需读取");
                return LinuxMetrics.Parse(command.Result);
            }
            finally { timeout.Dispose(); retained.Dispose(); monitorOperation.Release(); }
        }, CancellationToken.None);
        // Observe late exceptions, including channel-open failures after the visible deadline.
        _ = worker.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { return await worker.WaitAsync(requestToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !transportClosed.IsCancellationRequested) { throw new TimeoutException("监控超过8秒；已停止当前请求，其他通道保持。未完成的通道开通会继续收尾，不叠加请求。"); }
    }
    /// <summary>Runs one command on its own exec channel; never touches an interactive PTY. Output is
    /// capped at <paramref name="maxOutputBytes"/> and the call is bounded by <paramref name="timeout"/>.
    /// Callers build the command from fixed text and validated values only.</summary>
    public async Task<ExecResult> ExecAsync(string command, TimeSpan timeout, CancellationToken ct, int maxOutputBytes = 1024 * 1024)
    {
        ct.ThrowIfCancellationRequested();
        var retained = Retain();
        var work = CancellationTokenSource.CreateLinkedTokenSource(ct, transportClosed.Token);
        work.CancelAfter(timeout);
        var workToken = work.Token;
        var worker = Task.Run(async () =>
        {
            try
            {
                using var exec = client.CreateCommand(command);
                exec.MaxOutputBytes = maxOutputBytes;
                exec.CommandTimeout = timeout;
                await exec.ExecuteAsync(workToken).ConfigureAwait(false);
                workToken.ThrowIfCancellationRequested();
                return new ExecResult(exec.ExitStatus, exec.Result, exec.Error);
            }
            finally { work.Dispose(); retained.Dispose(); }
        }, CancellationToken.None);
        _ = worker.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        using var visible = CancellationTokenSource.CreateLinkedTokenSource(ct, transportClosed.Token);
        visible.CancelAfter(timeout + TimeSpan.FromSeconds(1));
        try { return await worker.WaitAsync(visible.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !transportClosed.IsCancellationRequested) { throw new TimeoutException("命令超过时限，已停止等待；其他通道保持。"); }
    }
    /// <summary>Local port forward (tunnel) on this transport. Binds only the given local address;
    /// the returned handle holds a lease on the transport until disposed.</summary>
    public LocalForward ForwardLocal(string bindHost, uint bindPort, string remoteHost, uint remotePort) =>
        Forward(new ForwardedPortLocal(bindHost, bindPort, remoteHost, remotePort), p => ((ForwardedPortLocal)p).BoundPort);
    /// <summary>Dynamic (SOCKS4/5, no authentication) forward: each client connection opens its own
    /// direct-tcpip channel on this transport. Bind to loopback only.</summary>
    public LocalForward ForwardDynamic(string bindHost, uint bindPort) =>
        Forward(new ForwardedPortDynamic(bindHost, bindPort), p => ((ForwardedPortDynamic)p).BoundPort);
    LocalForward Forward(ForwardedPort port, Func<ForwardedPort, uint> boundPort)
    {
        IDisposable lease;
        try { lease = Retain(); } catch { port.Dispose(); throw; }
        try
        {
            client.AddForwardedPort(port);
            port.Start();
            return new LocalForward(client, port, boundPort, lease);
        }
        catch
        {
            try { client.RemoveForwardedPort(port); } catch (Exception) { }
            port.Dispose();
            lease.Dispose();
            throw;
        }
    }
    public void Dispose() => ReleaseOwner();
    int ownerReleased;
    void ReleaseOwner() { if (Interlocked.Exchange(ref ownerReleased, 1) == 0) Release(); }
    void Release() { lock (gate) { if (--references == 0) { closing = true; StopTransport(); } } }
}

public sealed record ExecResult(int? ExitStatus, string Output, string Error);

public sealed class LocalForward : IDisposable
{
    readonly SshClient client; readonly ForwardedPort port; readonly Func<ForwardedPort, uint> boundPort; readonly IDisposable lease; int disposed;
    internal LocalForward(SshClient client, ForwardedPort port, Func<ForwardedPort, uint> boundPort, IDisposable lease) { this.client = client; this.port = port; this.boundPort = boundPort; this.lease = lease; }
    /// <summary>The bound local port (the actual one when 0 was requested).</summary>
    public uint Port => boundPort(port);
    public bool IsStarted => Volatile.Read(ref disposed) == 0 && port.IsStarted;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { port.Stop(); } catch (Exception) { }
        try { client.RemoveForwardedPort(port); } catch (Exception) { }
        port.Dispose();
        lease.Dispose();
    }
}

public sealed class SftpLease : IRemoteFileSource
{
    internal SharedSshSession? Owner { get; }
    /// <summary>True when this file view shares an authenticated SSH transport that can also run the
    /// separate exec channel used by <see cref="SudoPermissionService"/>.</summary>
    public bool SupportsSameSshExec => Owner != null;
    public FileResource Resource { get; } = new();
    public WorkspaceProtocol Protocol => WorkspaceProtocol.Sftp;
    public SftpClient Client { get; }
    readonly IDisposable lease; readonly CancellationTokenRegistration ownerClosed;
    public SftpLease(SftpClient client, IDisposable lease, SessionConnectOptions? options = null, CancellationToken transportClosed = default, SharedSshSession? owner = null)
    { Owner = owner; Client = client; this.lease = lease; ownerClosed = transportClosed.Register(() => ThreadPool.QueueUserWorkItem(_ => { try { Dispose(); } catch { } })); if (options is { KeepAliveEnabled: true, SftpProbeEnabled: true }) _ = Heartbeat(options); }
    async Task Heartbeat(SessionConnectOptions options)
    {
        try { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.KeepAliveSeconds)); while (await timer.WaitForNextTickAsync(Closed)) { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Closed); timeout.CancelAfter(TimeSpan.FromSeconds(8)); string? directory; lock (directoryGate) directory = probeDirectory; if (directory == null && HasWorkingDirectory) directory = Client.WorkingDirectory; if (string.IsNullOrEmpty(directory)) continue; await Client.LStatRawAsync(directory, timeout.Token).WaitAsync(timeout.Token); } }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
        catch { if (closed.IsCancellationRequested) return; CloseReason = "SFTP只读保活无响应或通道不可用，已停止此文件通道；SSH其他能力保留。"; try { Dispose(); } catch { } }
    }
    public string? CloseReason { get; private set; }
    readonly object directoryGate = new(); readonly HashSet<string> explicitDirectories = new(StringComparer.Ordinal); string? probeDirectory;
    public bool HasWorkingDirectory => IsAvailable && !string.IsNullOrEmpty(Client.WorkingDirectory);
    public bool IsDirectoryAuthorized(string absolute)
    {
        if (HasWorkingDirectory) return true; if (!absolute.StartsWith('/')) return false;
        lock (directoryGate) return explicitDirectories.Any(root => root == "/" || absolute == root || absolute.StartsWith(root + "/", StringComparison.Ordinal));
    }
    public void AuthorizeAbsoluteDirectory(string absolute)
    {
        if (!absolute.StartsWith('/') || absolute.Length > 16384 || absolute.Split('/').Contains("..") || absolute.Any(c => c < 32 || c == 127) || absolute.Contains('\\')) throw new IOException("请使用明确绝对目录");
        absolute = "/" + string.Join('/', absolute.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != "."));
        lock (directoryGate) { if (explicitDirectories.Count >= 128 && !explicitDirectories.Contains(absolute)) throw new IOException("绝对目录授权过多，请关闭并明确新建来源"); explicitDirectories.Add(absolute); }
    }
    internal void VerifiedDirectory(string absolute) { lock (directoryGate) probeDirectory = absolute; }
    public Guid SourceId { get; } = Guid.NewGuid();
    readonly CancellationTokenSource closed = new();
    public CancellationToken Closed => closed.Token;
    public bool IsAvailable => Volatile.Read(ref disposed) == 0 && Client.IsConnected;
    int disposed;
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { closed.Cancel(); ownerClosed.Dispose(); try { Client.Dispose(); } finally { lease.Dispose(); } } }
    // First vertical slice refuses overwrite. Uploads use exclusive creation;
    // downloads never truncate an existing local destination.
    public async Task UploadNewAsync(Stream input, string remote, CancellationToken ct)
    {
        var slash = remote.LastIndexOf('/'); var directory = slash < 0 ? "." : remote[..(slash + 1)];
        var temp = directory.TrimEnd('/') + "/.forterm-upload-" + Guid.NewGuid().ToString("N");
        bool created = false;
        try
        {
            await using (var output = await Client.OpenAsync(temp, FileMode.CreateNew, FileAccess.Write, ct))
            {
                created = true; await input.CopyToAsync(output, 32768, ct); await output.FlushAsync(ct);
            }
            // Standard SFTP v3 rename cannot replace an existing target. Do
            // not request the posix-rename extension or fallback to delete.
            if (await Task.Run(() => Client.Exists(remote), ct)) throw new IOException("目标已存在；α1拒绝覆盖。");
            await Client.RenameFileAsync(temp, remote, ct); created = false;
        }
        finally
        {
            if (created) { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try { await Client.DeleteFileAsync(temp, cleanup.Token); } catch {/* uncertain cleanup is reported by caller as failed, never success */} }
        }
    }

    public async Task DownloadNewAsync(string remote, string local, CancellationToken ct)
    {
        await using var output = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32768, true);
        await Client.DownloadFileAsync(remote, output, ct);
        await output.FlushAsync(ct);
    }
}
