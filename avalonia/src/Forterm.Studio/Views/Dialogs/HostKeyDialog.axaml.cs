using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Forterm.Core;

namespace Forterm.Studio.Views.Dialogs;

public sealed class HostKeyDialogModel(HostKeyQuestion question)
{
    public bool Changed { get; } = question.Changed;
    public string Title => Changed ? "主机指纹已变化" : "确认主机指纹";
    public string Endpoint { get; } = question.Endpoint;
    public string Previous { get; } = question.Previous ?? "";
    public string Current { get; } = question.Current;
    public string CurrentLabel => Changed ? "本次收到的指纹" : "主机指纹";
    public string TrustText => Changed ? "确认更换并连接" : "信任并连接";
    public string Explanation => Changed
        ? "该地址的主机指纹与之前信任的不同。可能是服务器重装或堡垒机更换了密钥，也可能是中间人攻击。请向管理员核对后再继续。"
        : "首次连接该地址。请与堡垒机或管理员提供的指纹核对，确认一致后再继续。";
}

/// <summary>First-use and changed host keys. Default answer (Esc, close) is reject.</summary>
public sealed partial class HostKeyDialog : Window
{
    bool trusted;

    public HostKeyDialog()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("TrustButton")!.Click += (_, _) => { trusted = true; Close(); };
        this.FindControl<Button>("RejectButton")!.Click += (_, _) => Close();
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        this.FindControl<Grid>("Header")!.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public static async Task<bool> AskAsync(Window? owner, HostKeyQuestion question, CancellationToken ct)
    {
        var dialog = new HostKeyDialog { DataContext = new HostKeyDialogModel(question) };
        await DialogHost.ShowAsync(dialog, owner, ct);
        return dialog.trusted && !ct.IsCancellationRequested;
    }
}
