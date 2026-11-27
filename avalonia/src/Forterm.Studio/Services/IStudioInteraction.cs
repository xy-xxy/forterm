using Forterm.Core;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Services;

/// <summary>Questions and pickers that need the user. Callers may invoke these from any
/// thread; implementations marshal to the UI thread themselves.</summary>
public interface IStudioInteraction
{
    /// <summary>Password or keyboard-interactive answers; null when the user cancels.
    /// Callers clear the returned array after use.</summary>
    Task<string[]?> AskCredentialsAsync(CredentialChallenge challenge, CancellationToken ct);

    /// <summary>First-use or changed host key. False rejects the connection.</summary>
    Task<bool> ConfirmHostKeyAsync(HostKeyQuestion question, CancellationToken ct);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger);

    /// <summary>Single-line text (group names); null when cancelled.</summary>
    Task<string?> PromptTextAsync(string title, string label, string initial);

    /// <summary>Shows the connection editor; true when the user saved.</summary>
    Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor);

    Task<string?> PickOpenFileAsync(string title);
    /// <summary>Several local files (upload); empty when cancelled.</summary>
    Task<string[]> PickOpenFilesAsync(string title);
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickSaveFileAsync(string title, string suggestedName);
    Task CopyTextAsync(string text);

    /// <summary>A short message in the corner of the window. Never the only place something important is said.</summary>
    void Toast(ToastMessage message);

    /// <summary>Shows a local folder in the system's file manager (explicit user action).</summary>
    Task OpenFolderAsync(string path);

    /// <summary>Opens the process manager window (non-modal). The window disposes the model when closed.</summary>
    void ShowProcesses(ProcessListViewModel processes);

    /// <summary>"修改权限": returns the requested normal bits (000–777), or null when cancelled.
    /// <paramref name="sudoAvailable"/> offers the sudo (root) option for SFTP over SSH.</summary>
    Task<PermissionRequest?> EditPermissionsAsync(PermissionSnapshot current, bool sudoAvailable);

    /// <summary>After <paramref name="after"/>, clears the clipboard only if it still holds <paramref name="expected"/>.</summary>
    void ClearClipboardLater(string expected, TimeSpan after);

    /// <summary>Opens an http(s) address in the system browser (explicit user action).</summary>
    Task OpenUrlAsync(string url);

    void ShowAccounts(AccountsViewModel accounts);

    /// <summary>"堡垒机资产" of an account (non-modal). The window disposes the model when closed.</summary>
    void ShowBastion(BastionAssetsViewModel assets);

    /// <summary>Opens the settings window (non-modal). The window disposes the model when closed.</summary>
    void ShowSettings(SettingsViewModel settings);

    /// <summary>Shows clipboard text before it goes into a terminal; true = paste it.</summary>
    Task<bool> ReviewPasteAsync(PasteReview review);

    /// <summary>Something with a × has just gone away in this window, and the next × has taken its place (see
    /// <see cref="CloseGuard"/>): that window's × do not react for a moment.</summary>
    void Moved();

    /// <summary>New/edit tunnel; null when cancelled.</summary>
    Task<Library.TunnelDefinition?> EditTunnelAsync(Library.TunnelDefinition? existing, IReadOnlyList<Library.StudioProfile> sshProfiles);
}

/// <summary>Requested permission change. <see cref="FileMode"/> applies to files inside a recursive
/// directory change; null keeps those files unchanged. <see cref="UseSudo"/> applies through sudo
/// on a separate exec channel of the same SSH connection (never the terminal).</summary>
public sealed record PermissionRequest(uint Mode, bool Recursive, uint? FileMode, bool UseSudo = false);
