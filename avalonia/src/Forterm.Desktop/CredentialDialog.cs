using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public static partial class CredentialDialogs
{
    public static async Task<string[]?> Ask(Window owner, CredentialChallenge challenge, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var window = new Window { Title = $"服务器验证 · 第{challenge.Round}轮", Width = 680, Height = 420, MinWidth = 400, MinHeight = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var entries = new List<TextBox>(); var body = new StackPanel { Spacing = 10, Margin = new Thickness(16, 12, 24, 16) };
        body.Children.Add(UI.Label(challenge.PasswordAlreadySubmitted ? "密码已提交；服务器要求追加验证，不会自动重用之前的内容。" : "服务器请求验证；仅用于当前认证。", true));
        if (challenge.Instruction.Length > 0) body.Children.Add(UI.Label(challenge.Instruction, true));
        foreach (var q in challenge.Questions) { body.Children.Add(UI.Label(q.Text)); var entry = new TextBox { PasswordChar = q.Echo ? '\0' : '●' }; entries.Add(entry); body.Children.Add(entry); }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        actions.Children.Add(UI.Button("取消", () => window.Close(null))); actions.Children.Add(UI.Button("继续", () => window.Close(entries.Select(e => e.Text ?? "").ToArray()), true));
        var root = new DockPanel(); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions); root.Children.Add(new ScrollViewer { Content = body }); window.Content = root;
        using var registration = ct.Register(() => Dispatcher.UIThread.Post(() => window.Close(null)));
        try { return await window.ShowDialog<string[]?>(owner); } finally { foreach (var entry in entries) entry.Text = ""; }
    }
}

public static partial class UI { public static Task<string[]?> AskCredentials(Window owner, CredentialChallenge challenge, CancellationToken ct) => CredentialDialogs.Ask(owner, challenge, ct); }

public static partial class UI
{
    public static Task<bool> VerifyFingerprint(Window owner, KnownHosts hosts, string host, int port, string fingerprint, bool persist, CancellationToken ct) => hosts.VerifyAsync(host, port, fingerprint, persist, (q, token) => Dispatcher.UIThread.InvokeAsync(() => Confirm(owner, q.Changed ? "主机指纹已变化，请重新核对" : "确认主机指纹", q.Changed ? $"{q.Endpoint}\n旧指纹：{q.Previous}\n新指纹：{q.Current}\n只在与可信来源核对后确认替换。" : $"{q.Endpoint}\n{q.Current}\n请与可信来源核对。", token)), ct);
}
