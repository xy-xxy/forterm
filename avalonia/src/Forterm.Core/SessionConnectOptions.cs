namespace Forterm.Core;

public sealed record SessionConnectOptions
{
    public SharedSshSession? Jump { get; init; }
    public Func<CancellationToken, Task<OpenSshAgent>>? AgentProvider { get; init; }
    public string AgentFingerprint { get; init; } = "";
    public bool KeepAliveEnabled { get; init; } = true;
    public int KeepAliveSeconds { get; init; } = 15;
    public bool SftpProbeEnabled { get; init; } = true;
    /// <summary>Socket connect and per-request timeout for the SSH transport (5–300 s).
    /// The overall authentication window (including user prompts) stays at least 45 s.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 45;
    /// <summary>How many session channels (terminals, SFTP, commands) may be open at once on the connection. One more
    /// waits, without a limit, until another is closed; with a higher number than the server's, the server refuses it.</summary>
    public int MaxChannels { get; init; } = 10;
    /// <summary>Outbound HTTP/SOCKS5 proxy for the first hop. Ignored when <see cref="Jump"/> is set:
    /// later hops always go through the authenticated jump transport.</summary>
    public ProxyEndpoint? Proxy { get; init; }
    /// <summary>Moved to the front of the client's offer ("" = default order); see <see cref="SshAlgorithms"/>.</summary>
    public string PreferredKeyExchange { get; init; } = "";
    public string PreferredCipher { get; init; } = "";
    /// <summary>Channels are asked for as OpenSSH and other common clients do: once, where the library repeats a refused
    /// request up to ten times at once; with packets of 32 KiB, where it says 64 KiB; and a terminal with a list of modes,
    /// where it sends an empty one.</summary>
    public bool CommonChannels { get; init; }
    /// <summary>Notes which messages are exchanged with the server (see <see cref="SshTrace"/>).</summary>
    public SshTrace? Trace { get; init; }
    public void Validate()
    {
        if (KeepAliveSeconds is < 5 or > 600) throw new ArgumentOutOfRangeException(nameof(KeepAliveSeconds), "心跳间隔必须5–600秒");
        if (ConnectTimeoutSeconds is < 5 or > 300) throw new ArgumentOutOfRangeException(nameof(ConnectTimeoutSeconds), "连接超时必须5–300秒");
        if (MaxChannels is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(MaxChannels), "通道上限必须1–1000");
        Proxy?.Validate();
        SshAlgorithms.Validate(PreferredKeyExchange ?? "", PreferredCipher ?? "");
    }
}

public enum ProxyKind { Socks5, Http }

public sealed record ProxyEndpoint(ProxyKind Kind, string Host, int Port, string User = "", string Password = "")
{
    public override string ToString() => $"{(Kind == ProxyKind.Http ? "http" : "socks5")}://{Host}:{Port}";
    public void Validate()
    {
        if (Host.Length == 0 || Host.Length > 255 || Host.IndexOfAny([' ', '\t', '\r', '\n', '/', '@']) >= 0) throw new ArgumentException("代理地址无效");
        if (Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(Port), "代理端口无效");
    }
}
