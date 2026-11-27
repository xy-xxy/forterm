using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Forterm.Studio.Services;

/// <summary>Commands that run on a channel of their own on the session's connection (exec): "监控" and ending a process
/// from it, server-side "压缩", sudo permissions. Some bastions do not allow them: one closed the whole connection,
/// terminal and all, 0.1 s after the request (2026-11-27). So in a controller-launched session the user is told so once
/// before the first, and a server that ended the connection right after one is remembered (by a hash of its address)
/// and asked about first in every later session, also of a saved connection.</summary>
public sealed class CommandChannels(IStudioInteraction ui, bool launched, string host, int port, string? directory)
{
    /// <summary>A connection that ends this soon after a command channel was opened is taken to have been ended by it.</summary>
    internal static TimeSpan Soon { get; set; } = TimeSpan.FromSeconds(3);
    const string FileName = "command-channel-ends.json";
    const int MostRemembered = 500;
    static readonly object gate = new();

    readonly string key = Key(host, port);
    bool allowed;
    long openedAt;

    /// <summary>This server ended a connection right after a command channel before.</summary>
    public bool EndedBefore => Remembered(directory).Contains(key);

    /// <summary>Whether a command channel may be opened now for <paramref name="what"/> (a short Chinese phrase, "读取监控").
    /// Not <paramref name="byUser"/> (refreshing by itself): never asks; goes on only where nothing needs asking.</summary>
    public async Task<bool> AllowAsync(string what, string runs, bool byUser = true)
    {
        bool ended = EndedBefore;
        if (!allowed && (launched || ended))
        {
            if (!byUser) return false;
            string message = $"{what}会在这个连接上另开一个命令通道，执行{runs}，不在终端里输入。\n" +
                "有些堡垒机不允许这样执行命令，或按命令审计规则拦截，会直接断开整个连接（终端也会断开），之后只能从控制器重新发起会话。" +
                (ended ? "\n这台服务器上次就在执行命令后不久断开了连接。" : "") + "\n本会话中不再询问。";
            if (!await ui.ConfirmAsync(ended ? "这台服务器上次执行命令后断开了连接" : "在这个连接上执行命令", message, "仍然执行", danger: ended)) return false;
            allowed = true;
        }
        Interlocked.Exchange(ref openedAt, Environment.TickCount64);
        return true;
    }

    /// <summary>A command allowed before starts now (a queued one may start later than it was allowed).</summary>
    public void Opening() => Interlocked.Exchange(ref openedAt, Environment.TickCount64);

    /// <summary>The connection has ended: right after a command channel, that server is remembered.</summary>
    public void ConnectionEnded()
    {
        long at = Interlocked.Read(ref openedAt);
        if (at == 0 || Environment.TickCount64 - at > (long)Soon.TotalMilliseconds) return;
        Remember(directory, key);
    }

    /// <summary>Not the address itself: what is kept says only "this one", for whoever reads the data directory.</summary>
    internal static string Key(string host, int port) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("forterm-command-ends:" + host.Trim().ToLowerInvariant() + ":" + port)));

    static HashSet<string> Remembered(string? directory)
    {
        if (directory == null) return [];
        lock (gate)
        {
            try
            {
                string path = Path.Combine(directory, FileName);
                if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return [];
                var keys = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
                return keys.Where(k => k is { Length: 64 } && k.All(char.IsAsciiHexDigit)).ToHashSet(StringComparer.Ordinal);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
        }
    }

    static void Remember(string? directory, string key)
    {
        if (directory == null) return;
        lock (gate)
        {
            var keys = Remembered(directory);
            if (!keys.Add(key)) return;
            string path = Path.Combine(directory, FileName), temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(temp, JsonSerializer.Serialize(keys.TakeLast(MostRemembered).ToArray()));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
        }
    }
}
