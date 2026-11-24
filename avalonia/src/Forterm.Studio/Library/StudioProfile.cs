using System.Text.Json.Serialization;

namespace Forterm.Studio.Library;

/// <summary>A saved connection. Secrets are never part of this record: the password and key
/// passphrase live in the OS credential store, and only the HasPassword/HasPassphrase markers
/// are kept here.</summary>
public sealed record StudioProfile
{
    public string Id { get; init; } = NewId();
    public string Name { get; init; } = "";
    /// <summary>ssh, sftp, ftp or telnet.</summary>
    public string Protocol { get; init; } = "ssh";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    public string User { get; init; } = "";
    /// <summary>password, keyboard, key or agent.</summary>
    public string AuthMode { get; init; } = "password";
    public string KeyPath { get; init; } = "";
    public bool HasPassword { get; init; }
    public bool HasPassphrase { get; init; }
    public string GroupId { get; init; } = "";
    public string Note { get; init; } = "";
    public bool Favorite { get; init; }

    // 连接设置
    /// <summary>0 = default (45 s).</summary>
    public int TimeoutMs { get; init; }
    /// <summary>SSH protocol keepalive; 0 = off. Default 15 s.</summary>
    public int HeartbeatMs { get; init; } = 15000;
    public string Encoding { get; init; } = "UTF-8";
    /// <summary>Empty = xterm-256color.</summary>
    public string TerminalType { get; init; } = "";
    /// <summary>"禁用 pwd 自动跳转": no shell hook, the file tree does not follow the terminal.</summary>
    public bool DisablePwdFollow { get; init; }
    /// <summary>"禁止列表监控开启": the library's info column never shows this host, and its monitor
    /// column starts in manual refresh (no automatic exec probes).</summary>
    public bool DisableListMonitor { get; init; }
    /// <summary>Algorithm preferences moved to the front of the offer; "" = default order.
    /// (No host key preference: see <see cref="Forterm.Core.SshAlgorithms"/>.)</summary>
    public string Cipher { get; init; } = "";
    public string KeyExchange { get; init; } = "";

    // 跳板机 / 代理
    /// <summary>Saved SSH connections to go through, in order: the first connects first.</summary>
    public IReadOnlyList<string> JumpChain { get; init; } = Array.Empty<string>();
    /// <summary>socks5://host:port or http://host:port; empty = direct.</summary>
    public string Proxy { get; init; } = "";
    public bool ProxyDisabled { get; init; }

    // 初始化
    public string DefaultPath { get; init; } = "";
    /// <summary>Sent to the interactive shell once after it opens (user-configured terminal write).</summary>
    public string InitialScript { get; init; } = "";
    /// <summary>Sent to the interactive shell every <see cref="KeepAliveCommandMs"/> (user-configured).</summary>
    public string KeepAliveCommand { get; init; } = "";
    public int KeepAliveCommandMs { get; init; }
    /// <summary>"会话环境变量": SSH "env" requests on every interactive shell of this connection.</summary>
    public IReadOnlyList<EnvironmentEntry> SessionEnvironment { get; init; } = Array.Empty<EnvironmentEntry>();

    // 使用记录
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsed { get; init; }
    public int UseCount { get; init; }

    [JsonIgnore] public string Endpoint => $"{Host}:{Port}";
    [JsonIgnore] public bool IsSsh => Protocol is "ssh" or "sftp";
    [JsonIgnore] public string ProtocolLabel => Protocol switch { "sftp" => "SFTP", "ftp" => "FTP", "telnet" => "Telnet", _ => "SSH" };

    public static string NewId() => Guid.NewGuid().ToString("N");
    public static int DefaultPort(string protocol) => protocol switch { "ftp" => 21, "telnet" => 23, _ => 22 };
}

public sealed record ProfileGroup(string Id, string Name, string ParentId = "");

public sealed record EnvironmentEntry(string Name, string Value);

/// <summary>隧道: a port forward through a saved SSH connection. Always binds 127.0.0.1.
/// "local" forwards to RemoteHost:RemotePort; "dynamic" is a SOCKS4/5 proxy (no remote target).</summary>
public sealed record TunnelDefinition
{
    public const string Local = "local", Dynamic = "dynamic";
    public string Id { get; init; } = StudioProfile.NewId();
    public string Name { get; init; } = "";
    public string Kind { get; init; } = Local;
    public string ProfileId { get; init; } = "";
    public int LocalPort { get; init; }
    public string RemoteHost { get; init; } = "127.0.0.1";
    public int RemotePort { get; init; }
    public string Note { get; init; } = "";
    [JsonIgnore] public bool IsDynamic => Kind == Dynamic;
}

public sealed record LibraryDocument
{
    public int Version { get; init; } = 1;
    public List<ProfileGroup> Groups { get; init; } = new();
    public List<StudioProfile> Profiles { get; init; } = new();
    public List<TunnelDefinition> Tunnels { get; init; } = new();
}
