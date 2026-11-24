using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FluentFTP;
using FluentFTP.Exceptions;

namespace Forterm.Core;

public sealed record FtpStamp(long Length, DateTime Modified, FtpObjectType Type, int Mode, string Owner, string Group);
public sealed record FtpVersion(string Path, FtpStamp? Stamp, string? Hash);
public sealed record FtpDirectory(FtpFileSession Source, Guid SourceId, string Path);
public sealed record FtpListing(FtpDirectory Directory, IReadOnlyList<RemoteEntry> Entries);
public sealed class FtpPolicyException(string message) : IOException(message);

/// <summary>One authenticated FTP control connection; serial commands, no reconnect or credential replay.
/// FTP has no generic atomic-exclusive rename/replace guarantee. Existing destinations are refused.</summary>
public sealed partial class FtpFileSession : IRemoteFileSource
{
    readonly Transport transport;
    readonly CancellationTokenSource closed;
    readonly CancellationToken closedToken;
    int disposed;
    public Guid SourceId { get; } = Guid.NewGuid();
    public CancellationToken Closed => closedToken;
    public FileResource Resource => transport.Resource;
    public WorkspaceProtocol Protocol => WorkspaceProtocol.Ftp;
    public string? CloseReason => transport.CloseReason;
    public bool IsAvailable
    {
        get { if (Volatile.Read(ref disposed) != 0) return false; if (transport.IsAvailable) return true; transport.Stop("FTP连接已断开；不会自动重用凭据。"); return false; }
    }
    FtpFileSession(Transport transport)
    {
        this.transport = transport; transport.Retain(); closed = CancellationTokenSource.CreateLinkedTokenSource(transport.Closed); closedToken = closed.Token;
    }
    public FtpFileSession CreateSibling() { Ensure(); return new(transport); }
    void Ensure() { if (!IsAvailable) throw new OperationCanceledException("原FTP来源已停止，不自动重连或重用凭据"); }
    public static async Task<FtpFileSession> ConnectAsync(string host, int port, string user, string password, CancellationToken ct, SessionConnectOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(host) || host.IndexOfAny(['/', '\\', '\0', '\r', '\n']) >= 0 || port is < 1 or > 65535 || user.Length > 1024 || password.Length > 32768 || user.Any(c => c < 32 || c == 127) || password.Any(c => c < 32 || c == 127)) throw new FtpPolicyException("FTP地址、端口或认证字段无效");
        options ??= new(); options.Validate(); if (options.Jump != null) throw new FtpPolicyException("FTP尚不支持SSH跳板；未绕过跳板直连");
        var config = new FtpConfig
        {
            SelfConnectMode = FtpSelfConnectMode.Never,
            SslSessionLength = 0,
            RetryAttempts = 1,
            ConnectTimeout = 15000,
            ReadTimeout = 8000,
            WriteTimeout = 8000,
            DataConnectionConnectTimeout = 8000,
            DataConnectionReadTimeout = 30000,
            DataConnectionWriteTimeout = 30000,
            Noop = false,
            LogToConsole = false,
            LogHost = false,
            LogUserName = false,
            LogPassword = false,
            SanitizeUrlEncoding = false,
            DisconnectWithQuit = false
        };
        var client = new StrictClient(host, user, password, port, config) { Encoding = new UTF8Encoding(false, true) };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await client.Connect(timeout.Token).ConfigureAwait(false); client.UseBoundPassive(); client.Config.ListingParser = FtpParser.Auto;
            string cwd = Absolute(await client.GetWorkingDirectory(timeout.Token).ConfigureAwait(false), "/");
            var connection = new Transport(client, cwd); var source = new FtpFileSession(connection);
            if (options.KeepAliveEnabled) _ = connection.Heartbeat(options.KeepAliveSeconds);
            return source;
        }
        catch (OperationCanceledException) { client.Dispose(); throw; }
        catch (Exception) { client.Dispose(); throw new IOException("FTP连接或认证失败；没有重试或重用凭据。"); }
        finally { password = ""; }
    }
    public static string Absolute(string path, string cwd)
    {
        if (path.Length > 16384 || path.Any(c => c < 32 || c == 127) || path.Contains('\\')) throw new FtpPolicyException("FTP路径包含控制字符、反斜线或过长");
        if (!path.StartsWith('/')) path = cwd.TrimEnd('/') + "/" + path;
        var pieces = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Contains("..")) throw new FtpPolicyException("请使用明确的绝对路径，不接受父跳转");
        return "/" + string.Join('/', pieces.Where(p => p != "."));
    }
    static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || name.Any(c => c < 32 || c == 127) || Encoding.UTF8.GetByteCount(name) > 1024) throw new FtpPolicyException("FTP文件名无效");
        return name;
    }
    void Bound(FtpDirectory directory) { Ensure(); if (!ReferenceEquals(directory.Source, this) || directory.SourceId != SourceId) throw new FtpPolicyException("FTP来源不匹配，未改投目标"); }
    async Task<T> Run<T>(Func<StrictClient, CancellationToken, Task<T>> action, CancellationToken ct, bool data = false)
    {
        Ensure(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, Closed); timeout.CancelAfter(data ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(8));
        await transport.Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try { Ensure(); return await action(transport.Client, timeout.Token).ConfigureAwait(false); }
        catch (FtpPolicyException) { throw; }
        catch (FtpCommandException e) { if (e.CompletionCode == "421" || !transport.Client.IsConnected) transport.Stop("FTP服务端已关闭连接；不自动重新认证。"); throw new IOException("FTP请求被服务器明确拒绝（" + e.CompletionCode + "）"); }
        catch { transport.Stop("FTP请求未完成或协议状态不明，已停止此连接；不自动重用凭据。"); throw; }
        finally { transport.Gate.Release(); }
    }
    public Task<FtpListing> ListAsync(string path, CancellationToken ct) => Run(async (client, token) =>
    {
        string absolute = Absolute(path, transport.Cwd); var items = await ListChecked(client, absolute, token);
        return new FtpListing(new(this, SourceId, absolute), items.Select(f => new RemoteEntry(absolute.TrimEnd('/') + "/" + Name(f.Name), f.Name, f.Type == FtpObjectType.Directory, f.Type == FtpObjectType.Link, f.Type == FtpObjectType.File, f.Size) { Modified = f.Modified > DateTime.UnixEpoch ? new DateTimeOffset(DateTime.SpecifyKind(f.Modified, DateTimeKind.Utc)) : null }).OrderByDescending(f => f.Directory && !f.Link).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }, ct);
    public async Task<FtpDirectory> CaptureDirectoryAsync(string path, CancellationToken ct) => (await ListAsync(path, ct)).Directory;
    async Task<FtpListItem[]> ListChecked(StrictClient client, string absolute, CancellationToken ct)
    {
        string normalized = Absolute(absolute, transport.Cwd); if (normalized != absolute) throw new FtpPolicyException("FTP目录路径不明确");
        string parent = "/"; var parts = absolute.Split('/', StringSplitOptions.RemoveEmptyEntries); if (parts.Length > 128) throw new FtpPolicyException("FTP目录层级超过128层");
        foreach (string part in parts)
        {
            var matches = (await client.ListStrict(parent, ct)).Where(f => f.Name == part).ToArray();
            if (matches.Length != 1 || matches[0].Type != FtpObjectType.Directory || !string.IsNullOrEmpty(matches[0].LinkTarget)) throw new FtpPolicyException("FTP父目录不可核对、含链接或不是目录；未跟随链接操作");
            parent = parent.TrimEnd('/') + "/" + part;
        }
        return await client.ListStrict(absolute, ct);
    }
    async Task<FtpVersion> Capture(StrictClient client, FtpDirectory directory, string name, CancellationToken ct, long maxBytes = long.MaxValue)
    {
        Bound(directory); string filename = Name(name), path = directory.Path.TrimEnd('/') + "/" + filename;
        var items = await ListChecked(client, directory.Path, ct); var matches = items.Where(f => f.Name == filename).ToArray();
        if (matches.Length > 1) throw new FtpPolicyException("FTP目录返回重复名称，无法确定目标");
        if (matches.Length == 0) return new(path, null, null);
        var item = matches[0]; if (item.Type != FtpObjectType.File || item.Size < 0 || item.Size > maxBytes) throw new FtpPolicyException("FTP只支持已知大小的普通文件；编辑限1MiB，不跟随链接");
        var stamp = Stamp(item); string hash = await client.HashStrict(path, stamp.Length, ct);
        var after = (await ListChecked(client, directory.Path, ct)).SingleOrDefault(f => f.Name == filename);
        if (after == null || Stamp(after) != stamp) throw new FtpPolicyException("FTP文件在核对期间发生变化");
        return new(path, stamp, hash);
    }
    static FtpStamp Stamp(FtpListItem item) => new(item.Size, item.Modified, item.Type, item.Chmod, item.RawOwner ?? "", item.RawGroup ?? "");
    public Task<FtpVersion> CaptureAsync(FtpDirectory directory, string name, CancellationToken ct, long maxBytes = long.MaxValue) => Run((client, token) => Capture(client, directory, name, token, maxBytes), ct, true);
    async Task Check(StrictClient client, FtpDirectory directory, FtpVersion expected, CancellationToken ct)
    {
        Bound(directory); if (expected.Path != directory.Path.TrimEnd('/') + "/" + Name(expected.Path[(expected.Path.LastIndexOf('/') + 1)..])) throw new FtpPolicyException("FTP路径不属于原目录");
        if (expected != await Capture(client, directory, Path.GetFileName(expected.Path), ct, expected.Stamp?.Length ?? long.MaxValue)) throw new FtpPolicyException("FTP目标属性或内容已变化，未继续");
    }
    public Task<byte[]> ReadBoundedAsync(FtpDirectory directory, FtpVersion expected, int maxBytes, CancellationToken ct) => Run(async (client, token) =>
    {
        if (expected.Stamp == null || expected.Stamp.Length > maxBytes) throw new FtpPolicyException("FTP文件超过内存读取边界");
        await Check(client, directory, expected, token);
        var bytes = await client.ReadStrict(expected.Path, maxBytes, token);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != expected.Hash) throw new FtpPolicyException("FTP内容在读取期间变化");
        await Check(client, directory, expected, token); return bytes;
    }, ct, true);
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; closed.Cancel(); transport.Release(); }

    sealed class Transport(StrictClient client, string cwd)
    {
        public readonly FileResource Resource = new();
        public readonly StrictClient Client = client; public readonly string Cwd = cwd; public readonly SemaphoreSlim Gate = new(1, 1);
        readonly CancellationTokenSource closed = new(); public CancellationToken Closed => closed.Token;
        readonly object sync = new(); int references, stopped; public string? CloseReason { get; private set; }
        public bool IsAvailable => Volatile.Read(ref stopped) == 0 && Client.IsConnected;
        public void Retain() { lock (sync) { if (!IsAvailable) throw new IOException("FTP连接已关闭"); references++; } }
        public void Release() { lock (sync) if (--references == 0) Stop("FTP来源已关闭"); }
        public void Stop(string reason) { if (Interlocked.Exchange(ref stopped, 1) != 0) return; CloseReason = reason; closed.Cancel(); try { Client.Dispose(); } catch { } }
        public async Task Heartbeat(int seconds)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
                while (await timer.WaitForNextTickAsync(Closed))
                {
                    if (!await Gate.WaitAsync(0, Closed)) continue;
                    try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Closed); timeout.CancelAfter(TimeSpan.FromSeconds(8)); if (!(await Client.Execute("NOOP", timeout.Token)).Success) throw new IOException(); }
                    finally { Gate.Release(); }
                }
            }
            catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
            catch { Stop("FTP协议保活失败，已停止此连接，不自动重新认证。"); }
        }
    }
    sealed class StrictClient(string host, string user, string password, int port, FtpConfig config) : AsyncFtpClient(host, user, password, port, config)
    {
        public void UseBoundPassive() => Config.DataConnectionType = m_stream.RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? FtpDataConnectionType.EPSV : FtpDataConnectionType.PASVEX;
        public async Task CompleteData(CancellationToken ct)
        {
            // FtpDataStream.CloseAsync swallows TimeoutException upstream. Dispose only the data
            // socket, then explicitly read and validate the completion reply without that helper.
            var reply = await GetReply(ct); if (reply.Code is not ("226" or "250")) throw new IOException("FTP数据完成回执未确认");
        }
        public async Task<FtpListItem[]> ListStrict(string path, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8)); ct = timeout.Token;
            await SetDataTypeNoLockAsync(FtpDataType.Binary, ct);
            byte[] bytes;
            await using (var stream = await OpenDataStreamAsync("LIST " + path, 0, ct)) { bytes = await Read(stream, 4 * 1024 * 1024, ct); }
            await CompleteData(ct);
            var lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n'); if (lines.Length > 20002) throw new FtpPolicyException("FTP目录超过20000项");
            var items = new List<FtpListItem>();
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r'); if (line.Length == 0 || line.StartsWith("total ", StringComparison.Ordinal) && long.TryParse(line.AsSpan(6), out _)) continue;
                if (line.Length > 16384) throw new FtpPolicyException("FTP目录记录过长");
                var item = CurrentListParser.ParseSingleLine(path, line, m_capabilities, false);
                if (item == null) throw new FtpPolicyException("FTP列表格式不受支持；没有把未知条目当成不存在");
                if (item.Name is "." or "..") continue; Name(item.Name); items.Add(item);
            }
            return items.ToArray();
        }
        public async Task<string> HashStrict(string path, long expectedLength, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
            await using (var stream = await OpenRead(path, FtpDataType.Binary, 0, expectedLength == 0 ? -1 : expectedLength, ct))
            {
                byte[] buffer = new byte[32768]; while (true) { using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct); idle.CancelAfter(TimeSpan.FromSeconds(30)); int n = await stream.ReadAsync(buffer, idle.Token); if (n == 0) break; if (count > expectedLength - n) throw new IOException("FTP文件超过核对长度"); count += n; hash.AppendData(buffer, 0, n); }
            }
            await CompleteData(ct); if (count != expectedLength) throw new FtpPolicyException("FTP长度在读取期间发生变化"); return Convert.ToHexString(hash.GetHashAndReset());
        }
        public async Task<byte[]> ReadStrict(string path, int maxBytes, CancellationToken ct)
        {
            byte[] bytes; await using (var stream = await OpenRead(path, FtpDataType.Binary, 0, -1L, ct)) { bytes = await Read(stream, maxBytes, ct); }
            await CompleteData(ct); return bytes;
        }
        static async Task<byte[]> Read(Stream stream, int maxBytes, CancellationToken ct)
        {
            using var output = new MemoryStream(); byte[] block = new byte[32768];
            while (true) { using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct); idle.CancelAfter(TimeSpan.FromSeconds(30)); int n = await stream.ReadAsync(block, idle.Token); if (n == 0) break; if (output.Length + n > maxBytes) throw new IOException("FTP数据超过读取边界"); await output.WriteAsync(block.AsMemory(0, n), ct); }
            return output.ToArray();
        }
    }
}
