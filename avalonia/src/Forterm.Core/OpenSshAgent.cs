using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Security;
namespace Forterm.Core;

/// <summary>Read identities and request SSH authentication signatures from an
/// already-running local agent. Never starts an agent or exports private keys.</summary>
public sealed class OpenSshAgent : IDisposable
{
    readonly Stream stream; readonly SemaphoreSlim gate = new(1, 1); readonly CancellationToken lifetime; int disposed;
    public OpenSshAgent(Stream stream, CancellationToken lifetime) { this.stream = stream; this.lifetime = lifetime; }
    public static async Task<OpenSshAgent> ConnectDefaultAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", "openssh-ssh-agent", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(ct); return new(pipe, ct); } catch { pipe.Dispose(); throw; }
        }
        string path = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK") ?? throw new IOException("未设置SSH_AUTH_SOCK；不会自动安装或启动agent");
        return await ConnectUnixAsync(path, ct);
    }
    public static async Task<OpenSshAgent> ConnectUnixAsync(string path, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct); return new(new NetworkStream(socket, true), ct); } catch { socket.Dispose(); throw; }
    }
    async Task<byte[]> Exchange(byte[] request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime); timeout.CancelAfter(TimeSpan.FromSeconds(45)); await gate.WaitAsync(timeout.Token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this); byte[] header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, request.Length); await stream.WriteAsync(header, timeout.Token); await stream.WriteAsync(request, timeout.Token); await stream.FlushAsync(timeout.Token);
            await stream.ReadExactlyAsync(header, timeout.Token); int n = BinaryPrimitives.ReadInt32BigEndian(header); if (n < 1 || n > 256 * 1024) throw new IOException("agent响应超出256KiB边界"); byte[] reply = new byte[n]; await stream.ReadExactlyAsync(reply, timeout.Token); if (reply[0] == 5) throw new IOException("agent拒绝签名或需要当前不支持的会话/目的地约束；不回退其他凭据"); return reply;
        }
        finally { gate.Release(); }
    }
    public sealed record Identity(byte[] PublicKey, string Algorithm, string Fingerprint, string Comment);
    public async Task<IReadOnlyList<Identity>> IdentitiesAsync(CancellationToken ct)
    {
        byte[] response = await Exchange(new byte[] { 11 }, ct); var r = new Reader(response); if (r.Byte() != 12) throw new IOException("agent响应类型无效"); uint count = r.UInt(); if (count > 64) throw new IOException("agent钥匙超过64把，请减少或选择明确指纹"); var keys = new List<Identity>();
        for (uint i = 0; i < count; i++) { byte[] key = r.String(16384); string comment = Encoding.UTF8.GetString(r.String(4096)); var keyReader = new Reader(key); string algorithm = Encoding.ASCII.GetString(keyReader.String(128)); keys.Add(new(key, algorithm, "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('='), comment.Length > 256 ? comment[..256] : comment)); }
        r.End(); return keys;
    }
    async Task<byte[]> SignAsync(byte[] key, byte[] data, string algorithm, CancellationToken ct)
    {
        if (data.Length > 128 * 1024) throw new IOException("SSH签名数据超出边界"); uint flags = algorithm == "rsa-sha2-512" ? 4u : algorithm == "rsa-sha2-256" ? 2u : 0u;
        using var w = new MemoryStream(); w.WriteByte(13); WriteString(w, key); WriteString(w, data); WriteUInt(w, flags); byte[] response = await Exchange(w.ToArray(), ct); var r = new Reader(response); if (r.Byte() != 14) throw new IOException("agent签名响应无效"); byte[] signature = r.String(65536); r.End(); var signed = new Reader(signature); string returned = Encoding.ASCII.GetString(signed.String(128)); if (returned != algorithm) throw new IOException("agent返回了不同签名算法，拒绝静默降级"); return signature;
    }
    public async Task<IReadOnlyList<IPrivateKeySource>> SourcesAsync(string fingerprint, CancellationToken ct)
    {
        var identities = await IdentitiesAsync(ct); if (fingerprint.Length > 0) identities = identities.Where(k => k.Fingerprint == fingerprint).ToArray(); if (identities.Count == 0) throw new IOException("agent未找到可用钥匙或指定指纹");
        using var none = new NoneAuthenticationMethod("agent-verifier"); var policy = new ConnectionInfo("unused", 22, "agent-verifier", none); var sources = new List<IPrivateKeySource>();
        foreach (var key in identities)
        {
            string[] algorithms = key.Algorithm == "ssh-rsa" ? new[] { "rsa-sha2-512", "rsa-sha2-256", "ssh-rsa" } : new[] { key.Algorithm }; var hosts = new List<HostAlgorithm>();
            foreach (string algorithm in algorithms) { if (!policy.HostKeyAlgorithms.TryGetValue(algorithm, out var verifier) || algorithm.Contains("-cert-") || algorithm.StartsWith("sk-")) continue; hosts.Add(new AgentAlgorithm(algorithm, key.PublicKey, this, verifier(key.PublicKey), ct)); }
            if (hosts.Count > 0) sources.Add(new AgentSource(hosts));
        }
        if (sources.Count == 0) throw new IOException("agent钥匙类型尚未支持；不导出私钥或回退密码"); return sources;
    }
    sealed class AgentSource(IReadOnlyCollection<HostAlgorithm> algorithms) : IPrivateKeySource { public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms => algorithms; }
    sealed class AgentAlgorithm(string name, byte[] key, OpenSshAgent agent, KeyHostAlgorithm verifier, CancellationToken ct) : HostAlgorithm(name)
    {
        public override byte[] Data => key;
        public override byte[] Sign(byte[] data) { var signature = agent.SignAsync(key, data, Name, ct).GetAwaiter().GetResult(); if (!verifier.VerifySignature(data, signature)) throw new IOException("agent签名校验失败"); return signature; }
        public override bool VerifySignature(byte[] data, byte[] signature) => verifier.VerifySignature(data, signature);
    }
    static void WriteUInt(Stream s, uint n) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); s.Write(b); }
    static void WriteString(Stream s, byte[] b) { WriteUInt(s, (uint)b.Length); s.Write(b); }
    sealed class Reader(byte[] data)
    {
        int offset;
        public byte Byte() { if (offset >= data.Length) throw new IOException("agent响应截断"); return data[offset++]; }
        public uint UInt() { if (data.Length - offset < 4) throw new IOException("agent响应截断"); uint n = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)); offset += 4; return n; }
        public byte[] String(int limit) { uint n = UInt(); if (n > limit || n > data.Length - offset) throw new IOException("agent字段长度无效"); byte[] result = data.AsSpan(offset, (int)n).ToArray(); offset += (int)n; return result; }
        public void End() { if (offset != data.Length) throw new IOException("agent响应包含额外字段"); }
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) stream.Dispose(); }
}
