using System.Text;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Xunit;

namespace Forterm.Studio.Tests;

public sealed class LibraryStoreTests
{
    [Fact]
    public void SavesAtomicallyAndReloadsWithoutSecrets()
    {
        using var h = new StudioHarness();
        var group = h.Store.AddGroup("生产", "");
        var p = new StudioProfile { Name = "应用1", Host = "192.0.2.10", Port = 22, User = "root", GroupId = group.Id, HasPassword = true, Note = "第一行\n第二行" };
        h.Store.Upsert(p);
        string json = File.ReadAllText(Path.Combine(h.Data, "library.json"));
        Assert.DoesNotContain("synthetic-only", json);
        Assert.Empty(Directory.GetFiles(h.Data, "*.tmp"));
        var reloaded = new LibraryStore(h.Data);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(p), System.Text.Json.JsonSerializer.Serialize(reloaded.Find(p.Id)));
        Assert.Equal("生产", reloaded.GroupPath(group.Id));
    }

    [Fact]
    public void InvalidChangesAreRejectedAndLeaveTheFileUntouched()
    {
        using var h = new StudioHarness();
        h.Store.Upsert(new StudioProfile { Name = "ok", Host = "192.0.2.1", User = "u" });
        byte[] before = File.ReadAllBytes(Path.Combine(h.Data, "library.json"));
        Assert.Throws<InvalidDataException>(() => h.Store.Upsert(new StudioProfile { Host = "bad host", User = "u" }));
        Assert.Throws<InvalidDataException>(() => h.Store.Upsert(new StudioProfile { Host = "h", User = "u", Port = 70000 }));
        Assert.Throws<InvalidDataException>(() => h.Store.Upsert(new StudioProfile { Protocol = "ftp", Port = 21, Host = "h", User = "u", AuthMode = "key", KeyPath = "/k" }));
        Assert.Throws<InvalidDataException>(() => h.Store.Upsert(new StudioProfile { Host = "h", User = "u", DefaultPath = "relative" }));
        Assert.Throws<InvalidDataException>(() => h.Store.Upsert(new StudioProfile { Host = "h", User = "u", Proxy = "socks5://user:pw@proxy:1080" }));
        Assert.Throws<InvalidDataException>(() => h.Store.Upsert(new StudioProfile { Host = "h", User = "u", JumpChain = ["missing"] }));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(h.Data, "library.json")));
    }

    [Fact]
    public void RemovingAGroupMovesItsContentsUpAndRemovingAJumpPrunesChains()
    {
        using var h = new StudioHarness();
        var parent = h.Store.AddGroup("YiXun", "");
        var child = h.Store.AddGroup("子组", parent.Id);
        var jump = new StudioProfile { Name = "跳板", Host = "192.0.2.1", User = "j", GroupId = child.Id };
        h.Store.Upsert(jump);
        var target = new StudioProfile { Name = "目标", Host = "10.0.0.5", User = "t", JumpChain = [jump.Id] };
        h.Store.Upsert(target);
        h.Store.RemoveGroup(child.Id);
        Assert.Equal(parent.Id, h.Store.Find(jump.Id)!.GroupId);
        h.Store.Remove(new HashSet<string> { jump.Id });
        Assert.Empty(h.Store.Find(target.Id)!.JumpChain);
    }

    [Fact]
    public void ExportHasNoSecretsAndImportGetsNewIdentities()
    {
        using var h = new StudioHarness();
        var group = h.Store.AddGroup("组", "");
        var jump = h.Ssh(2222, "跳板");
        var target = new StudioProfile { Name = "目标", Host = "10.0.0.5", User = "t", JumpChain = [jump.Id], GroupId = group.Id, HasPassword = true, Favorite = true,
            InitialScript = "uptime", KeepAliveCommand = "date", KeepAliveCommandMs = 60000, DefaultPath = "/srv" };
        h.Store.Upsert(target);
        string file = Path.Combine(h.Data, "export.json");
        File.WriteAllBytes(file, LibraryExchange.Export(h.Store, [jump.Id, target.Id]));
        string text = File.ReadAllText(file);
        Assert.DoesNotContain("\"hasPassword\": true", text);
        Assert.DoesNotContain("synthetic-only", text);

        using var other = new StudioHarness();
        Assert.Equal(2, LibraryExchange.Import(other.Store, file, out int cleared));
        Assert.Equal(1, cleared);
        var importedTarget = other.Store.Profiles.Single(p => p.Name == "目标");
        var importedJump = other.Store.Profiles.Single(p => p.Name == "跳板");
        Assert.NotEqual(target.Id, importedTarget.Id);
        Assert.Equal([importedJump.Id], importedTarget.JumpChain);
        Assert.False(importedTarget.HasPassword);
        Assert.False(importedTarget.Favorite);
        Assert.Equal("组", other.Store.GroupPath(importedTarget.GroupId));
        // Commands typed into a terminal automatically are never taken over from someone's file.
        Assert.Equal(("", "", 0, "/srv"), (importedTarget.InitialScript, importedTarget.KeepAliveCommand, importedTarget.KeepAliveCommandMs, importedTarget.DefaultPath));
    }

    [Theory]
    [InlineData("socks5://127.0.0.1:1080", ProxyKind.Socks5, "127.0.0.1", 1080)]
    [InlineData("http://proxy.example:8080/", ProxyKind.Http, "proxy.example", 8080)]
    [InlineData("socks://[::1]:9050", ProxyKind.Socks5, "::1", 9050)]
    public void ProxyUrlsParse(string text, ProxyKind kind, string host, int port)
    {
        var proxy = ProxyParser.Parse(text);
        Assert.Equal((kind, host, port), (proxy.Kind, proxy.Host, proxy.Port));
    }

    [Theory]
    [InlineData("127.0.0.1:1080")]
    [InlineData("ftp://h:21")]
    [InlineData("socks5://h")]
    [InlineData("socks5://h:0")]
    public void BadProxyUrlsAreRejected(string text) => Assert.Throws<InvalidDataException>(() => ProxyParser.Parse(text));

    [Fact]
    public async Task GbkTerminalTranscodingSurvivesSplitCharacters()
    {
        var gbk = Encodings.Get("GBK");
        byte[] server = gbk.GetBytes("中文测试 ok\r\n");
        var inner = new FakePty(server);
        var pty = new TranscodingPtyConnection(inner, gbk);
        var output = new MemoryStream();
        var buffer = new byte[64];
        int n;
        while ((n = await pty.ReaderStream.ReadAsync(buffer)) > 0) output.Write(buffer, 0, n);
        Assert.Equal("中文测试 ok\r\n", Encoding.UTF8.GetString(output.ToArray()));
        byte[] typed = Encoding.UTF8.GetBytes("你好");
        pty.WriterStream.Write(typed, 0, 1);
        pty.WriterStream.Write(typed, 1, typed.Length - 1);
        Assert.Equal(gbk.GetBytes("你好"), inner.Written.ToArray());
    }

    /// <summary>Reader yields one byte per read, so every multi-byte character is split.</summary>
    sealed class FakePty(byte[] output) : Porta.Pty.IPtyConnection
    {
        public MemoryStream Written { get; } = new();
        public Stream ReaderStream { get; } = new OneByteStream(output);
        public Stream WriterStream => Written;
        public bool SupportsCancellableRead => true;
        public int Pid => 0;
        public int ExitCode => 0;
        public event EventHandler<Porta.Pty.PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public void Resize(int cols, int rows) { }
        public bool WaitForExit(int milliseconds) => true;
        public void Kill() { }
        public void Dispose() { }
    }

    sealed class OneByteStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct);
    }

    [Fact]
    public async Task LatencyIsTcpOnly()
    {
        using var fixture = LoopbackFixture.Ssh();
        Assert.NotNull(await LatencyProbe.MeasureAsync("127.0.0.1", fixture.Port, TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(0, fixture.Stats().GetProperty("auth").GetInt32());
        var closed = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        closed.Start();
        int port = ((System.Net.IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        Assert.Null(await LatencyProbe.MeasureAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), CancellationToken.None));
    }
}
