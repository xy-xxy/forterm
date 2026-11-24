using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Forterm.Core;

public sealed record HostKeyQuestion(string Endpoint, string? Previous, string Current) { public bool Changed => Previous != null && Previous != Current; }
public sealed class KnownHosts(string? directory)
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    readonly SemaphoreSlim gate = gates.GetOrAdd(directory == null ? "memory" : Path.GetFullPath(directory), _ => new(1, 1));
    static string Key(string host, int port) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host.Trim().ToLowerInvariant() + ":" + port)));
    Dictionary<string, string> Read() { if (directory == null) return new(); string path = Path.Combine(directory, "known-hosts.json"); if (!File.Exists(path)) return new(); if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("主机指纹库超过1MiB边界"); return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); }
    public async Task<bool> VerifyAsync(string host, int port, string fingerprint, bool persist, Func<HostKeyQuestion, CancellationToken, Task<bool>> ask, CancellationToken ct)
    {
        string key = Key(host, port); string? old; await gate.WaitAsync(ct); try { Read().TryGetValue(key, out old); } finally { gate.Release(); }
        if (old == fingerprint) return true; if (!await ask(new(host + ":" + port, old, fingerprint), ct)) return false;
        if (!persist || directory == null) return true;
        await gate.WaitAsync(ct); try
        {
            ct.ThrowIfCancellationRequested(); var data = Read(); data.TryGetValue(key, out var latest); if (latest != old && latest != fingerprint) throw new IOException("核对期间主机指纹被其他操作更新，请重新确认"); data[key] = fingerprint; Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "known-hosts.json"), temp = Path.Combine(directory, "known-hosts." + Guid.NewGuid().ToString("N") + ".tmp");
            try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(data), ct); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
        finally { gate.Release(); }
    }
}
