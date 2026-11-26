using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views.Dialogs;

namespace Forterm.Studio.Services;

/// <summary>Shows Studio's dialogs and pickers on the UI thread over the owning window.</summary>
public sealed class DialogInteraction(Func<Window?> owner) : IStudioInteraction
{
    public Task<string[]?> AskCredentialsAsync(CredentialChallenge challenge, CancellationToken ct) =>
        Dispatcher.UIThread.InvokeAsync(() => CredentialDialog.AskAsync(owner(), challenge, "", ct));

    public Task<bool> ConfirmHostKeyAsync(HostKeyQuestion question, CancellationToken ct) =>
        Dispatcher.UIThread.InvokeAsync(() => HostKeyDialog.AskAsync(owner(), question, ct));

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger) =>
        Dispatcher.UIThread.InvokeAsync(() => SimpleDialog.ConfirmAsync(owner(), title, message, confirmText, danger));

    public Task<string?> PromptTextAsync(string title, string label, string initial) =>
        Dispatcher.UIThread.InvokeAsync(() => SimpleDialog.PromptAsync(owner(), title, label, initial));

    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) =>
        Dispatcher.UIThread.InvokeAsync(() => ConnectionEditorDialog.ShowAsync(owner(), editor));

    /// <summary>The system's pickers cannot be covered by the lock screen: what one gives back after Studio locked, or
    /// while it is locked, is not used (somebody else may have chosen it).</summary>
    static bool Locked(int locks) => AppLock.IsLocked || AppLock.Locks != locks;

    public Task<string?> PickOpenFileAsync(string title) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner() is not { } window) return null;
        int locks = AppLock.Locks;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        try { return files.Count == 0 || Locked(locks) ? null : files[0].TryGetLocalPath(); }
        finally { foreach (var file in files) file.Dispose(); }
    });

    public Task<string[]> PickOpenFilesAsync(string title) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner() is not { } window) return Array.Empty<string>();
        int locks = AppLock.Locks;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = true });
        try { return Locked(locks) ? Array.Empty<string>() : files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray(); }
        finally { foreach (var file in files) file.Dispose(); }
    });

    public Task<string?> PickFolderAsync(string title) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner() is not { } window) return null;
        int locks = AppLock.Locks;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        try { return folders.Count == 0 || Locked(locks) ? null : folders[0].TryGetLocalPath(); }
        finally { foreach (var folder in folders) folder.Dispose(); }
    });

    public Task<string?> PickSaveFileAsync(string title, string suggestedName) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner() is not { } window) return null;
        int locks = AppLock.Locks;
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName, ShowOverwritePrompt = true });
        try { return Locked(locks) ? null : file?.TryGetLocalPath(); }
        finally { file?.Dispose(); }
    });

    public void ShowProcesses(ProcessListViewModel processes) => Dispatcher.UIThread.Post(() =>
    {
        var window = new Views.ProcessWindow { DataContext = processes };
        window.Closed += (_, _) => processes.Dispose();
        if (owner() is { } o) window.Show(o); else window.Show();
    });

    public Task<PermissionRequest?> EditPermissionsAsync(PermissionSnapshot current, bool sudoAvailable) =>
        Dispatcher.UIThread.InvokeAsync(() => PermissionDialog.AskAsync(owner(), current, sudoAvailable));

    public void ClearClipboardLater(string expected, TimeSpan after) => _ = Task.Run(async () =>
    {
        await Task.Delay(after);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (owner()?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() == expected) await clipboard.SetTextAsync("");
        });
    });

    public Task OpenUrlAsync(string url) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner() is { } window && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            await window.Launcher.LaunchUriAsync(uri);
    });

    public void ShowAccounts(AccountsViewModel accounts) => Dispatcher.UIThread.Post(() =>
    {
        var window = new Views.AccountsWindow { DataContext = accounts };
        window.Closed += (_, _) => accounts.Dispose();
        if (owner() is { } o) window.Show(o); else window.Show();
    });

    public void ShowSettings(SettingsViewModel settings) => Dispatcher.UIThread.Post(() =>
    {
        var window = new Views.SettingsWindow { DataContext = settings };
        if (owner() is { } o) window.Show(o); else window.Show();
    });

    public Task<bool> ReviewPasteAsync(PasteReview review) => Dispatcher.UIThread.InvokeAsync(() => PasteDialog.AskAsync(owner(), review));

    public Task<Library.TunnelDefinition?> EditTunnelAsync(Library.TunnelDefinition? existing, IReadOnlyList<Library.StudioProfile> sshProfiles) =>
        Dispatcher.UIThread.InvokeAsync(() => TunnelDialog.AskAsync(owner(), existing, sshProfiles));

    public void Toast(ToastMessage message) => Dispatcher.UIThread.Post(() => (owner()?.DataContext as MainWindowViewModel)?.Toasts.Show(message));

    public Task OpenFolderAsync(string path) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner() is { } window && Directory.Exists(path)) await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    });

    public Task CopyTextAsync(string text) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (owner()?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    });
}
