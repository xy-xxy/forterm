using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.ViewModels;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.4: the connection form's remaining screenshot items (algorithm preferences,
/// session environment variables, "禁止列表监控开启") and dynamic (SOCKS) tunnels.</summary>
public sealed class Alpha4Tests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Stopping a tunnel tears the forward down off the UI thread; wait for the listener to go.</summary>
    internal static async Task AssertClosedSoon(int port)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try { using var c = new TcpClient(); await c.ConnectAsync("127.0.0.1", port); }
            catch (SocketException) { return; }
            Assert.True(DateTime.UtcNow < end, "tunnel port still accepts connections after stop");
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
    }

    static int FreePort()
    {
        using var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task AlgorithmPreferencesLeadNegotiationWithoutRestrictingIt()
    {
        using var fixture = LoopbackFixture.Ssh();
        async Task<SshNegotiated> Connect(SessionConnectOptions options)
        {
            using var session = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only",
                (_, _) => Task.FromResult(true), CancellationToken.None, options: options);
            return session.Negotiated;
        }

        JsonElement Server() => fixture.Stats().GetProperty("negotiated").EnumerateArray().Last();
        var defaults = await Connect(new SessionConnectOptions());
        Assert.Equal("aes128-ctr", defaults.Cipher);
        Assert.Equal("rsa-sha2-512", defaults.HostKey);
        Assert.NotEqual("ecdh-sha2-nistp384", defaults.KeyExchange);
        // SSH.NET names the implementation, so curve25519-sha256@libssh.org reports as curve25519-sha256.
        Assert.StartsWith(defaults.KeyExchange, Server().GetProperty("kex").GetString());

        var chosen = await Connect(new SessionConnectOptions { PreferredCipher = "aes256-ctr", PreferredKeyExchange = "ecdh-sha2-nistp384" });
        // The host key type never changes with these preferences (one fingerprint per host:port).
        Assert.Equal(new SshNegotiated("ecdh-sha2-nistp384", "rsa-sha2-512", "aes256-ctr"), chosen);
        // The server agrees independently.
        var server = Server();
        Assert.Equal("ecdh-sha2-nistp384", server.GetProperty("kex").GetString());
        Assert.Equal("rsa-sha2-512", server.GetProperty("hostKey").GetString());
        Assert.Equal("aes256-ctr", server.GetProperty("cipher").GetString());

        // A preference the server lacks only reorders the offer: negotiation falls back to the defaults.
        Assert.Equal("aes128-ctr", (await Connect(new SessionConnectOptions { PreferredCipher = "chacha20-poly1305@openssh.com" })).Cipher);
        // An unknown name is refused before any network activity.
        int connections = fixture.Stats().GetProperty("connections").GetInt32();
        await Assert.ThrowsAsync<ArgumentException>(() => Connect(new SessionConnectOptions { PreferredCipher = "rot13" }));
        Assert.Equal(connections, fixture.Stats().GetProperty("connections").GetInt32());
    }

    [Fact]
    public void EnvironmentAndAlgorithmFieldsAreValidated()
    {
        static void Bad(params (string, string)[] vars) =>
            Assert.Throws<ArgumentException>(() => ShellEnvironment.Validate(vars.Select(v => KeyValuePair.Create(v.Item1, v.Item2)).ToArray()));
        Bad(("1BAD", "x"));
        Bad(("A-B", "x"));
        Bad(("", "x"));
        Bad(("LANG", "a"), ("LANG", "b"));
        Bad(("LANG", "zh\nexport X=1"));
        Bad(("LANG", "\u001b[2J"));
        Bad(("LANG", new string('x', ShellEnvironment.MaxValueLength + 1)));
        Bad(Enumerable.Range(0, ShellEnvironment.MaxEntries + 1).Select(i => ("V" + i, "x")).ToArray());
        ShellEnvironment.Validate([KeyValuePair.Create("LANG", "zh_CN.UTF-8"), KeyValuePair.Create("_X1", "带 空格 的值")]);

        var ssh = new StudioProfile { Name = "a", Host = "127.0.0.1", User = "u" };
        LibraryStore.ValidateProfile(ssh with { Cipher = "aes256-ctr", KeyExchange = "curve25519-sha256", SessionEnvironment = [new("LANG", "C")] });
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateProfile(ssh with { Cipher = "rot13" }));
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateProfile(ssh with { SessionEnvironment = [new("bad name", "x")] }));
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateProfile(ssh with { SessionEnvironment = [null!] }));
        var telnet = ssh with { Protocol = "telnet", Port = 23 };
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateProfile(telnet with { SessionEnvironment = [new("LANG", "C")] }));
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateProfile(telnet with { Cipher = "aes256-ctr" }));
        // Regression: an invalid TERM used to escape as ArgumentException (unhandled in the save button).
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateProfile(ssh with { TerminalType = "xterm 256" }));
    }

    [AvaloniaFact]
    public async Task SessionEnvironmentIsSentAsProtocolRequestsOnEveryShell()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_ACCEPT_ENV"] = "LANG,FORTERM_TEST" });
        using var h = new StudioHarness();
        var p = h.Ssh(fixture.Port, "环境变量") with
        {
            DisablePwdFollow = true,
            SessionEnvironment = [new("LANG", "zh_CN.UTF-8"), new("FORTERM_TEST", "值 1"), new("SECRET_X", "refused")],
        };
        h.Store.Upsert(p);
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.IsConnected, w.Status);
        await Pump(() => w.HasShell, TimeSpan.FromSeconds(10));
        var env = fixture.Stats().GetProperty("env").EnumerateArray().ToList();
        Assert.Equal(["LANG", "FORTERM_TEST", "SECRET_X"], env.Select(e => e.GetProperty("name").GetString()));
        Assert.Equal("值 1", env[1].GetProperty("value").GetString());
        Assert.Equal([true, true, false], env.Select(e => e.GetProperty("accepted").GetBoolean()));
        Assert.All(env, e => Assert.False(e.GetProperty("afterShell").GetBoolean()));
        Assert.Contains("SECRET_X", w.Status);
        Assert.Contains("AcceptEnv", w.Status);
        Assert.DoesNotContain("LANG", w.Status);

        // A second terminal is its own channel and gets the same requests.
        await w.AddTerminalAsync();
        await Pump(() => fixture.Stats().GetProperty("env").GetArrayLength() >= 6, TimeSpan.FromSeconds(10));
        var all = fixture.Stats();
        Assert.Equal(6, all.GetProperty("env").GetArrayLength());
        Assert.Equal(2, all.GetProperty("env").EnumerateArray().Select(e => e.GetProperty("channel").GetInt32()).Distinct().Count());
        // Nothing was typed: the variables never appear in terminal input.
        string typed = string.Concat(all.GetProperty("input").EnumerateArray().Select(i => i.GetString()));
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes("FORTERM_TEST")).ToLowerInvariant(), typed);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes("export")).ToLowerInvariant(), typed);
    }

    [AvaloniaFact]
    public async Task ListMonitorSwitchHidesInfoAndKeepsMonitorManual()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var hidden = h.Ssh(fixture.Port, "不显示") with { DisableListMonitor = true };
        h.Store.Upsert(hidden);
        Dispatcher.UIThread.RunJobs();
        var w = await h.Model.OpenProfileAsync(hidden);
        Assert.True(w.IsConnected, w.Status);
        Assert.False(w.Monitor!.AutoRefresh);
        int execs = fixture.Stats().GetProperty("execs").GetInt32();
        await Task.Delay(3500);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(execs, fixture.Stats().GetProperty("execs").GetInt32());
        // A manual refresh still works in the monitor column, but the list never shows the host.
        await w.Monitor.RefreshAsync();
        await w.Monitor.RefreshAsync();
        Assert.NotNull(w.Monitor.Summary);
        h.Model.Home.RefreshInfo();
        Assert.Equal("", h.Model.Home.Rows.Single(r => r.Name == "不显示").Info);
    }

    [AvaloniaFact]
    public async Task DynamicTunnelServesSocks5ThroughSavedConnection()
    {
        using var jumpHost = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_FORWARD"] = "1" });
        using var target = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        h.Ssh(jumpHost.Port, "代理主机");
        int localPort = FreePort();
        var tunnels = h.Model.Home.Tunnels!;
        h.Interaction.TunnelAnswer = (_, ssh) => new TunnelDefinition { Name = "SOCKS", Kind = TunnelDefinition.Dynamic, ProfileId = ssh.Single().Id, LocalPort = localPort, RemoteHost = "", RemotePort = 0 };
        tunnels.NewCommand.Execute(null);
        await Pump(() => tunnels.Rows.Count == 1, TimeSpan.FromSeconds(3));
        var row = tunnels.Rows.Single();
        Assert.Equal("SOCKS 动态转发", row.Remote);
        await tunnels.StartAsync(row);
        Assert.True(row.IsRunning, row.Status);
        Assert.Contains("SOCKS", row.Status);

        async Task<(byte Reply, string Banner)> Socks(int port, byte last = 1)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", localPort);
            var stream = client.GetStream();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token);
            var method = new byte[2];
            await stream.ReadExactlyAsync(method, timeout.Token);
            Assert.Equal(new byte[] { 5, 0 }, method);
            await stream.WriteAsync(new byte[] { 5, 1, 0, 1, 127, 0, 0, last, (byte)(port >> 8), (byte)port }, timeout.Token);
            var reply = new byte[10];
            await stream.ReadExactlyAsync(reply, timeout.Token);
            if (reply[1] != 0) return (reply[1], "");
            var buffer = new byte[64];
            int n = await stream.ReadAsync(buffer, timeout.Token);
            return (0, Encoding.ASCII.GetString(buffer, 0, n));
        }
        var (ok, banner) = await Socks(target.Port);
        Assert.Equal(0, ok);
        Assert.StartsWith("SSH-2.0", banner);
        Assert.Equal(1, jumpHost.Stats().GetProperty("forwarded").GetInt32());
        // A refused direct-tcpip open (the fixture only allows 127.0.0.1) becomes a SOCKS failure
        // reply; the tunnel keeps running. Nothing connects to 127.0.0.2.
        var (refused, _) = await Socks(target.Port, last: 2);
        Assert.NotEqual(0, refused);
        Assert.True(row.IsRunning);

        tunnels.Stop(row.Id, "已停止");
        Assert.False(row.IsRunning);
        // An immediate restart waits for the old forward to release the port.
        await tunnels.StartAsync(row);
        Assert.True(row.IsRunning, row.Status);
        Assert.Equal(0, (await Socks(target.Port)).Reply);
        tunnels.Stop(row.Id, "已停止");
        await AssertClosedSoon(localPort);
        // Stopping also closes the tunnel's own SSH connection, not just the listener.
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (jumpHost.Stats().GetProperty("activeTransports").GetInt32() > 0 && DateTime.UtcNow < end) await Task.Delay(50);
        Assert.Equal(0, jumpHost.Stats().GetProperty("activeTransports").GetInt32());
    }

    [Fact]
    public void TunnelKindsAreValidated()
    {
        var via = new StudioProfile { Name = "v", Host = "127.0.0.1", User = "u" };
        var local = new TunnelDefinition { Name = "t", ProfileId = via.Id, LocalPort = 10000, RemoteHost = "127.0.0.1", RemotePort = 22 };
        LibraryStore.ValidateTunnel(local, [via]);
        LibraryStore.ValidateTunnel(local with { Kind = TunnelDefinition.Dynamic, RemoteHost = "", RemotePort = 0 }, [via]);
        // Leftover target fields on a SOCKS tunnel (default RemoteHost, hand edits) are ignored, never fatal.
        LibraryStore.ValidateTunnel(new TunnelDefinition { Name = "s", Kind = TunnelDefinition.Dynamic, ProfileId = via.Id, LocalPort = 10001 }, [via]);
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateTunnel(local with { Kind = TunnelDefinition.Dynamic, LocalPort = 0 }, [via]));
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateTunnel(local with { Kind = "remote" }, [via]));
        Assert.Throws<InvalidDataException>(() => LibraryStore.ValidateTunnel(local with { Kind = null! }, [via]));
    }

    [AvaloniaFact]
    public async Task EditorSavesNewFieldsAndReportsInvalidInput()
    {
        using var h = new StudioHarness();
        var editor = new ConnectionEditorViewModel(h.Store, h.Credentials, h.Connector, h.Interaction, null)
        {
            Name = "表单",
            Host = "127.0.0.1",
            User = "synthetic",
            TerminalType = "xterm 256",
        };
        Assert.False(await editor.SaveAsync());
        Assert.Contains("终端类型", editor.Error);
        editor.TerminalType = "";

        editor.AddEnvironmentCommand.Execute(null);
        editor.EnvironmentRows[0].Value = "无名";
        Assert.False(await editor.SaveAsync());
        Assert.Contains("环境变量名", editor.Error);
        editor.EnvironmentRows[0].Name = " LANG ";
        editor.EnvironmentRows[0].Value = "C.UTF-8";
        editor.AddEnvironmentCommand.Execute(null); // an empty row is ignored
        editor.Cipher = editor.CipherChoices.Single(c => c.Value == "aes256-ctr");
        editor.KeyExchange = editor.KeyExchangeChoices.Single(c => c.Value == "ecdh-sha2-nistp384");
        editor.DisableListMonitor = true;
        Assert.True(await editor.SaveAsync(), editor.Error);
        var saved = h.Store.Profiles.Single();
        Assert.Equal([new EnvironmentEntry("LANG", "C.UTF-8")], saved.SessionEnvironment);
        Assert.Equal("aes256-ctr", saved.Cipher);
        Assert.Equal("ecdh-sha2-nistp384", saved.KeyExchange);
        Assert.True(saved.DisableListMonitor);
        Assert.Contains("较弱", editor.CipherChoices.Single(c => c.Value == "3des-cbc").Label);
        Assert.Contains("较弱", editor.KeyExchangeChoices.Single(c => c.Value == "diffie-hellman-group1-sha1").Label);
        Assert.DoesNotContain("较弱", editor.CipherChoices.Single(c => c.Value == "aes256-ctr").Label);
        Assert.Equal("aes256-ctr", Services.ProfileConnector.OptionsFor(saved).PreferredCipher);

        // Reopening shows the saved values; switching to Telnet drops SSH-only settings.
        var again = new ConnectionEditorViewModel(h.Store, h.Credentials, h.Connector, h.Interaction, saved);
        Assert.Equal("aes256-ctr", again.Cipher.Value);
        Assert.Equal("LANG", again.EnvironmentRows.Single().Name);
        again.Protocol = ConnectionEditorViewModel.ProtocolChoices.Single(c => c.Value == "telnet");
        Assert.True(await again.SaveAsync(), again.Error);
        var telnet = h.Store.Profiles.Single();
        Assert.Empty(telnet.SessionEnvironment);
        Assert.Equal("", telnet.Cipher);
        Assert.False(telnet.DisableListMonitor);

        // The row limit is enforced in the form.
        var many = new ConnectionEditorViewModel(h.Store, h.Credentials, h.Connector, h.Interaction, null);
        for (int i = 0; i < ShellEnvironment.MaxEntries + 1; i++) many.AddEnvironmentCommand.Execute(null);
        Assert.Equal(ShellEnvironment.MaxEntries, many.EnvironmentRows.Count);
        Assert.Contains("最多", many.Error);
        many.RemoveEnvironmentCommand.Execute(many.EnvironmentRows[0]);
        Assert.Equal("", many.Error);
    }

    [Fact]
    public void ExportsOmitAndImportsDropSessionEnvironment()
    {
        using var h = new StudioHarness();
        var p = new StudioProfile { Name = "导出", Host = "127.0.0.1", User = "u", Cipher = "aes256-ctr", SessionEnvironment = [new("APP_ENV", "do-not-export")] };
        h.Store.Upsert(p);
        byte[] bytes = LibraryExchange.Export(h.Store, [p.Id]);
        string text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("do-not-export", text);
        Assert.DoesNotContain("APP_ENV", text);
        Assert.Contains("aes256-ctr", text);

        // A hand-made export that carries variables and a weak algorithm: both are dropped and counted.
        var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        node["profiles"]![0]!["sessionEnvironment"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["name"] = "LD_PRELOAD", ["value"] = "/tmp/x.so" });
        node["profiles"]![0]!["keyExchange"] = "diffie-hellman-group1-sha1";
        string file = Path.Combine(h.Data, "import.json");
        File.WriteAllText(file, node.ToJsonString());
        using var other = new StudioHarness();
        Assert.Equal(1, LibraryExchange.Import(other.Store, file, out int cleared));
        Assert.Equal(1, cleared);
        var imported = other.Store.Profiles.Single();
        Assert.Empty(imported.SessionEnvironment);
        Assert.Equal("", imported.Cipher);
        Assert.Equal("", imported.KeyExchange);
        // An algorithm preference alone is also dropped and counted.
        node["profiles"]![0]!["sessionEnvironment"] = new System.Text.Json.Nodes.JsonArray();
        node["profiles"]![0]!["keyExchange"] = "";
        File.WriteAllText(file, node.ToJsonString());
        using var third = new StudioHarness();
        Assert.Equal(1, LibraryExchange.Import(third.Store, file, out int clearedAlgorithms));
        Assert.Equal(1, clearedAlgorithms);
        Assert.Equal("", third.Store.Profiles.Single().Cipher);
    }

    [Fact]
    public void LibraryWithAnAlgorithmThisBuildLacksStillLoads()
    {
        using var h = new StudioHarness();
        var p = new StudioProfile { Name = "旧算法", Host = "127.0.0.1", User = "u", Cipher = "aes256-ctr", KeyExchange = "curve25519-sha256" };
        h.Store.Upsert(p);
        string file = Path.Combine(h.Data, "library.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        node["profiles"]![0]!["cipher"] = "arcfour-removed-someday";
        node["tunnels"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
        { ["id"] = StudioProfile.NewId(), ["name"] = "SOCKS", ["kind"] = "dynamic", ["profileId"] = p.Id, ["localPort"] = 11080 });
        File.WriteAllText(file, node.ToJsonString());
        var reloaded = new LibraryStore(h.Data);
        var loaded = reloaded.Profiles.Single();
        Assert.Equal("", loaded.Cipher); // treated as default order
        Assert.Equal("curve25519-sha256", loaded.KeyExchange);
        Assert.True(reloaded.Tunnels.Single().IsDynamic);
        // A plainly broken value is still reported, not swallowed.
        node["profiles"]![0]!["cipher"] = null;
        File.WriteAllText(file, node.ToJsonString());
        Assert.Throws<InvalidDataException>(() => new LibraryStore(h.Data));
    }
}
