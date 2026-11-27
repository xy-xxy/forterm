using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using System.Collections.Concurrent;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;

[assembly: AvaloniaTestApplication(typeof(Forterm.Studio.Tests.TestApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
// UI tests share one dispatcher and several tests start processes or own named mutexes/pipes;
// running them one at a time removes scheduling-dependent stalls. The suite takes seconds.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Forterm.Studio.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class NetworkGuard
{
    /// <summary>Tests only ever talk to loopback fixtures: library latency probes for documentation
    /// addresses (198.51.100.x, 203.0.113.x, *.example) are answered as "timeout" without a socket.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void LoopbackOnly() =>
        LatencyProbe.AllowHost = host => host is "localhost" or "::1" || (System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip));
}

internal static class Paths
{
    /// <summary>The avalonia/ directory (holds global.json).</summary>
    public static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(Path.Combine(dir.FullName, "tooling"))) return dir.FullName;
            throw new DirectoryNotFoundException("avalonia root not found");
        }
    }

    public static string Python => Path.Combine(Root, "tooling", "fixture-venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python3");

    /// <summary>Software-rendered screenshots for side-by-side review with the reference images.
    /// These are not native-window or Windows verification.</summary>
    public static string Evidence
    {
        get { string path = Path.Combine(Root, "evidence", "studio-" + StudioPaths.Version); Directory.CreateDirectory(path); return path; }
    }

    public static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "forterm-studio-test-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(path);
        return path;
    }
}

/// <summary>Owned loopback fixture (tooling/ssh_fixture.py or ftp_fixture.py). Binds 127.0.0.1 only;
/// synthetic credentials; never contacts real hosts.</summary>
internal sealed class LoopbackFixture : IDisposable
{
    readonly Process process;
    public int Port { get; }
    public string Fingerprint { get; } = "";

    LoopbackFixture(string script, IDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo(Paths.Python) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(Paths.Root, "tooling", script));
        if (environment != null) foreach (var (k, v) in environment) start.Environment[k] = v;
        process = Process.Start(start)!;
        // Drain stderr continuously: paramiko/pyftpdlib log tracebacks there, and a full pipe
        // would block the fixture (and any test waiting for its reply).
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        string? line = ReadLine();
        if (line == null) throw new InvalidOperationException("fixture exited before reporting its port");
        var data = JsonDocument.Parse(line).RootElement;
        Port = data.GetProperty("port").GetInt32();
        if (data.TryGetProperty("fingerprint", out var fp)) Fingerprint = fp.GetString() ?? "";
    }

    public static LoopbackFixture Ssh(IDictionary<string, string>? environment = null) => new("ssh_fixture.py", environment);
    /// <summary>A stand-in for a fort bastion portal (HTTPS on 127.0.0.1, a certificate of its own).</summary>
    public static LoopbackFixture Fort(IDictionary<string, string>? environment = null) => new("fort_portal_fixture.py", environment);

    /// <summary>Counters recorded by the fixture (auth, shells, shell input as hex, forwards, TERM).</summary>
    public JsonElement Stats()
    {
        process.StandardInput.WriteLine("stats");
        process.StandardInput.Flush();
        return JsonDocument.Parse(ReadLine() ?? throw new InvalidOperationException("fixture exited")).RootElement.Clone();
    }

    /// <summary>A fixture stdin command (seed-tree, make-links, …); returns its one-line JSON reply.</summary>
    public JsonElement Command(string line)
    {
        process.StandardInput.WriteLine(line);
        process.StandardInput.Flush();
        return JsonDocument.Parse(ReadLine() ?? throw new InvalidOperationException("fixture exited")).RootElement.Clone();
    }

    /// <summary>Bounded read: a stuck fixture fails the test instead of hanging the run.</summary>
    string? ReadLine()
    {
        var read = process.StandardOutput.ReadLineAsync();
        if (!read.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("fixture did not answer within 15 seconds");
        return read.Result;
    }
    public static LoopbackFixture Ftp() => new("ftp_fixture.py", null);

    bool disposed;

    public void Dispose()
    {
        if (disposed) return; // a test may end the server early
        disposed = true;
        if (!process.HasExited)
        {
            process.StandardInput.WriteLine("stop");
            process.StandardInput.Flush();
            if (!process.WaitForExit(2000)) process.Kill(true);
        }
        process.Dispose();
    }
}

/// <summary>Scripted user: answers credential prompts from a queue, trusts host keys, and
/// answers confirm/prompt/editor/picker calls from settable values.</summary>
internal sealed class ScriptedInteraction(params string[][] answers) : IStudioInteraction
{
    readonly Queue<string[]> queue = new(answers);
    public List<CredentialChallenge> Challenges { get; } = new();
    public List<HostKeyQuestion> HostKeys { get; } = new();
    public List<string> Confirms { get; } = new();
    public bool TrustHostKeys { get; init; } = true;
    public bool ConfirmAnswer { get; set; } = true;
    public string? PromptAnswer { get; set; }
    public string? OpenFile { get; set; }
    public string? SaveFile { get; set; }
    public Func<ConnectionEditorViewModel, Task<bool>>? Editor { get; set; }
    public string Clipboard { get; private set; } = "";

    /// <summary>Runs when a confirmation is answered (simulates something changing right after it).</summary>
    public Action<string>? OnConfirm { get; set; }
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger) { Confirms.Add(title + "：" + message); OnConfirm?.Invoke(title); return Task.FromResult(ConfirmAnswer); }
    public Task<string?> PromptTextAsync(string title, string label, string initial) => Task.FromResult(PromptAnswer);
    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) => Editor?.Invoke(editor) ?? Task.FromResult(false);
    public Task<string?> PickOpenFileAsync(string title) => Task.FromResult(OpenFile);
    public string[] OpenFiles { get; set; } = Array.Empty<string>();
    public string? Folder { get; set; }
    public Task<string[]> PickOpenFilesAsync(string title) => Task.FromResult(OpenFiles);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);
    public Task<string?> PickSaveFileAsync(string title, string suggestedName) => Task.FromResult(SaveFile);
    public Task CopyTextAsync(string text) { Clipboard = text; return Task.CompletedTask; }
    public List<ToastMessage> Toasts { get; } = new();
    public void Toast(ToastMessage message) => Toasts.Add(message);
    /// <summary>The window it stands for; null: the one the user last acted in, as before windows were told apart.</summary>
    public Avalonia.Controls.TopLevel? Window { get; set; }
    public int MovedCount { get; private set; }
    public void Moved() { MovedCount++; CloseGuard.MovedIn(Window); }
    public List<string> OpenedFolders { get; } = new();
    public Task OpenFolderAsync(string path) { OpenedFolders.Add(path); return Task.CompletedTask; }
    public ProcessListViewModel? Processes { get; private set; }
    public void ShowProcesses(ProcessListViewModel processes) => Processes = processes;
    public PermissionRequest? PermissionAnswer { get; set; }
    public PermissionSnapshot? PermissionAsked { get; private set; }
    public bool? SudoOffered { get; private set; }
    public Task<PermissionRequest?> EditPermissionsAsync(PermissionSnapshot current, bool sudoAvailable) { PermissionAsked = current; SudoOffered = sudoAvailable; return Task.FromResult(PermissionAnswer); }
    public List<(string Expected, TimeSpan After)> ClipboardClears { get; } = new();
    public void ClearClipboardLater(string expected, TimeSpan after) => ClipboardClears.Add((expected, after));
    public List<string> OpenedUrls { get; } = new();
    public Task OpenUrlAsync(string url) { OpenedUrls.Add(url); return Task.CompletedTask; }
    public AccountsViewModel? AccountsShown { get; private set; }
    public void ShowAccounts(AccountsViewModel accounts) => AccountsShown = accounts;
    public SettingsViewModel? SettingsShown { get; private set; }
    public void ShowSettings(SettingsViewModel settings) { SettingsShown?.Dispose(); SettingsShown = settings; }
    public List<PasteReview> Pastes { get; } = new();
    public bool PasteAnswer { get; set; } = true;
    /// <summary>Runs before a paste is answered; lets a test keep the question open.</summary>
    public Func<Task>? OnPaste { get; set; }
    public async Task<bool> ReviewPasteAsync(PasteReview review) { Pastes.Add(review); if (OnPaste != null) await OnPaste(); return PasteAnswer; }
    public Func<TunnelDefinition?, IReadOnlyList<StudioProfile>, TunnelDefinition?>? TunnelAnswer { get; set; }
    public Task<TunnelDefinition?> EditTunnelAsync(TunnelDefinition? existing, IReadOnlyList<StudioProfile> sshProfiles) => Task.FromResult(TunnelAnswer?.Invoke(existing, sshProfiles));

    public Task<string[]?> AskCredentialsAsync(CredentialChallenge challenge, CancellationToken ct)
    {
        Challenges.Add(challenge);
        return Task.FromResult(queue.Count > 0 ? (string[]?)queue.Dequeue().ToArray() : null);
    }

    public Task<bool> ConfirmHostKeyAsync(HostKeyQuestion question, CancellationToken ct)
    {
        HostKeys.Add(question);
        return Task.FromResult(TrustHostKeys);
    }
}

/// <summary>Stands in for Keychain / Credential Manager in tests. Never touches the OS vault.</summary>
internal sealed class MemoryVault : ISecretVault
{
    public ConcurrentDictionary<string, byte[]> Items { get; } = new();
    public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(Items.TryGetValue(key, out var v) ? (byte[]?)v.ToArray() : null);
    /// <summary>Runs after a secret is written (simulates something changing while the OS store is busy).</summary>
    public Action<string>? OnWrite { get; set; }
    public Task WriteAsync(string key, byte[] secret, CancellationToken ct) { Items[key] = secret.ToArray(); OnWrite?.Invoke(key); return Task.CompletedTask; }
    public Task DeleteAsync(string key, CancellationToken ct) { Items.TryRemove(key, out _); return Task.CompletedTask; }
}

/// <summary>A Studio main view model on a throwaway data directory and in-memory vault.</summary>
internal sealed class StudioHarness : IDisposable
{
    public string Data { get; } = Paths.TempDirectory();
    public MemoryVault Vault { get; } = new();
    public ScriptedInteraction Interaction { get; }
    public LibraryStore Store { get; }
    public CredentialStore Credentials { get; }
    public KnownHosts KnownHosts { get; }
    readonly Lazy<MainWindowViewModel> model;
    /// <summary>Created on first use. View models touch Avalonia's UI dispatcher, which binds to the
    /// first thread that uses it, so only [AvaloniaFact] tests (on the UI thread) may read this.</summary>
    public MainWindowViewModel Model => model.Value;

    public StudioHarness(ScriptedInteraction? interaction = null)
    {
        Interaction = interaction ?? new ScriptedInteraction();
        Store = new LibraryStore(Data);
        Credentials = new CredentialStore(Vault);
        KnownHosts = new KnownHosts(Data);
        model = new Lazy<MainWindowViewModel>(() => new MainWindowViewModel(Interaction, KnownHosts, Store, Credentials));
    }

    public ProfileConnector Connector => new(Store, Credentials, KnownHosts, Interaction);

    public StudioProfile Ssh(int port, string name = "回环", bool savePassword = true, params string[] jumps)
    {
        var p = new StudioProfile { Name = name, Host = "127.0.0.1", Port = port, User = "synthetic", HasPassword = savePassword, JumpChain = jumps };
        if (savePassword) Credentials.SavePasswordAsync(p.Id, "synthetic-only", CancellationToken.None).GetAwaiter().GetResult();
        Store.Upsert(p);
        return p;
    }

    public void Dispose()
    {
        if (model.IsValueCreated) model.Value.Dispose();
        try { Directory.Delete(Data, true); } catch (IOException) { }
    }
}
