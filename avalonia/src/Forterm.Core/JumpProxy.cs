using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
namespace Forterm.Core;

/// <summary>Private one-target SOCKS relay protected by random per-connection
/// credentials. Uses an authenticated jump's direct-tcpip channel; never shell.</summary>
internal sealed class JumpProxy : IForwardedPort
{
    readonly SshClient jump; readonly string host; readonly int port; readonly TcpListener listener = new(IPAddress.Loopback, 0); readonly CancellationTokenSource lifetime = new(); readonly Task serve;
    Socket? active; int disposed, forwarding;
    public string Username { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public int Port { get; }
    public string? Failure { get; private set; }
    public event EventHandler? Closing;
    public JumpProxy(SshClient jump, string host, int port) { this.jump = jump; this.host = host; this.port = port; listener.Start(4); Port = ((IPEndPoint)listener.LocalEndpoint).Port; serve = Serve(); }
    async Task Serve()
    {
        for (int attempts = 0; attempts < 32 && !lifetime.IsCancellationRequested; attempts++)
        {
            Socket? socket = null; bool accepted = false;
            try
            {
                socket = await listener.AcceptSocketAsync(lifetime.Token); active = socket; using var stream = new NetworkStream(socket, false); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (!await Authenticate(stream, timeout.Token)) { socket.Dispose(); active = null; continue; }
                await RequireTarget(stream, timeout.Token); accepted = true; Interlocked.Exchange(ref forwarding, 1); listener.Stop();
                // The established bridge lives until this transport is disposed, not until
                // the authentication timeout token is cancelled after a successful login.
                await Task.Run(() => jump.BridgeOwnedSocket(host, (uint)port, this, socket, () => { stream.Write(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 }); stream.Flush(); }), CancellationToken.None);
                return;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception e) { if (accepted) Failure = e.Message; try { socket?.Send(new byte[] { 5, 2, 0, 1, 0, 0, 0, 0, 0, 0 }); } catch { } if (Volatile.Read(ref forwarding) != 0) return; }
            finally { socket?.Dispose(); active = null; }
        }
    }
    async Task<bool> Authenticate(Stream stream, CancellationToken ct)
    {
        byte[] head = new byte[2]; await stream.ReadExactlyAsync(head, ct); if (head[0] != 5 || head[1] == 0) return false; byte[] methods = new byte[head[1]]; await stream.ReadExactlyAsync(methods, ct); if (!methods.Contains((byte)2)) { await stream.WriteAsync(new byte[] { 5, 255 }, ct); return false; }
        await stream.WriteAsync(new byte[] { 5, 2 }, ct); await stream.ReadExactlyAsync(head, ct); if (head[0] != 1) return false; byte[] user = new byte[head[1]]; await stream.ReadExactlyAsync(user, ct); byte[] n = new byte[1]; await stream.ReadExactlyAsync(n, ct); byte[] password = new byte[n[0]]; await stream.ReadExactlyAsync(password, ct);
        bool match = CryptographicOperations.FixedTimeEquals(user, Encoding.ASCII.GetBytes(Username)) && CryptographicOperations.FixedTimeEquals(password, Encoding.ASCII.GetBytes(Password)); CryptographicOperations.ZeroMemory(user); CryptographicOperations.ZeroMemory(password); await stream.WriteAsync(new byte[] { 1, match ? (byte)0 : (byte)1 }, ct); return match;
    }
    async Task RequireTarget(Stream stream, CancellationToken ct)
    {
        byte[] head = new byte[4]; await stream.ReadExactlyAsync(head, ct); if (head[0] != 5 || head[1] != 1 || head[2] != 0) throw new IOException("Unsupported private relay request."); string requested;
        if (head[3] is 1 or 4) { byte[] ip = new byte[head[3] == 1 ? 4 : 16]; await stream.ReadExactlyAsync(ip, ct); requested = new IPAddress(ip).ToString(); }
        else if (head[3] == 3) { byte[] size = new byte[1]; await stream.ReadExactlyAsync(size, ct); byte[] name = new byte[size[0]]; await stream.ReadExactlyAsync(name, ct); requested = new UTF8Encoding(false, true).GetString(name); }
        else throw new IOException("Unsupported private relay address.");
        byte[] number = new byte[2]; await stream.ReadExactlyAsync(number, ct); int requestedPort = (number[0] << 8) | number[1]; string expected = IPAddress.TryParse(host, out var parsed) ? parsed.ToString() : host;
        if (requestedPort != port || !StringComparer.OrdinalIgnoreCase.Equals(requested, expected)) throw new IOException("Private relay target does not match the bound destination.");
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; lifetime.Cancel(); Closing?.Invoke(this, EventArgs.Empty); active?.Dispose(); listener.Stop(); }
}
