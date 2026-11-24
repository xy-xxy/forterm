using System.Net;
using System.Net.Sockets;
using Forterm.Core;
using Xunit;

namespace Forterm.Tests;

public sealed class TelnetAdapterTests
{
    [Fact]
    public async Task TelnetAdapterSharesTerminalStreamWithoutStartingShellOrSsh()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var telnet = await TelnetPtyConnection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, CancellationToken.None);
            using var peer = await accept;
            await peer.GetStream().WriteAsync("telnet-ready"u8.ToArray());
            byte[] received = new byte[12]; int count = await telnet.ReaderStream.ReadAsync(received);
            Assert.Equal("telnet-ready", System.Text.Encoding.UTF8.GetString(received, 0, count));
            await telnet.WriterStream.WriteAsync("client-input"u8.ToArray());
            byte[] echoed = new byte[12]; int echoedCount = await peer.GetStream().ReadAsync(echoed);
            Assert.Equal("client-input", System.Text.Encoding.UTF8.GetString(echoed, 0, echoedCount));
            Assert.Equal(0, telnet.Pid); Assert.Equal(-1, telnet.ExitCode);
        }
        finally { listener.Stop(); }
    }
}
