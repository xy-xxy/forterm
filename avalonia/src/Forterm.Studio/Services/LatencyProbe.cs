using System.Diagnostics;
using System.Net.Sockets;

namespace Forterm.Studio.Services;

/// <summary>Connection-list latency: time to complete a TCP handshake (including name
/// resolution). Sends no data and never authenticates.</summary>
public static class LatencyProbe
{
    /// <summary>Test hook: tests restrict probes to loopback so they never touch the network.</summary>
    internal static Func<string, bool> AllowHost { get; set; } = _ => true;

    public static async Task<int?> MeasureAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        if (!AllowHost(host)) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var clock = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(host, port, cts.Token);
            return (int)Math.Max(1, clock.ElapsedMilliseconds);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ArgumentException) { return null; }
        finally { try { socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { } }
    }
}
