using System.Text;
using Forterm.Core;

namespace Forterm.Studio.Launch;

public enum WorkspaceKind { Terminal, Sftp, Ftp, Telnet, Local }

public sealed record LaunchRoute(WorkspaceKind Kind, string Title);

/// <summary>Maps a parsed controller launch to the workspace it opens.
/// xshell + ssh:// → terminal workspace (terminal, files and monitor on one transport);
/// xftp or sftp:// → file workspace (terminal opens on demand on the same transport);
/// ftp:// → FTP file workspace. The protocol rewrite for xftp already happened in
/// <see cref="LaunchPlan.Parse"/>; this only decides layout and title.</summary>
public static class LaunchRouter
{
    public static LaunchRoute Route(LaunchConnection connection)
    {
        var kind = connection.Protocol switch
        {
            "ftp" => WorkspaceKind.Ftp,
            "sftp" => WorkspaceKind.Sftp,
            _ => WorkspaceKind.Terminal,
        };
        string title = CleanTitle(connection.Title);
        return new(kind, title.Length > 0 ? title : connection.Host);
    }

    /// <summary>-newtab values come from the controller; keep them printable and bounded.</summary>
    public static string CleanTitle(string raw)
    {
        var builder = new StringBuilder(Math.Min(raw.Length, 80));
        foreach (char c in raw.Trim())
        {
            if (builder.Length >= 80) break;
            if (!char.IsControl(c)) builder.Append(c);
        }
        return builder.ToString().Trim();
    }
}
