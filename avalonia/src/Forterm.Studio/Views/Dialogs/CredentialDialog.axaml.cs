using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Mvvm;

namespace Forterm.Studio.Views.Dialogs;

public sealed class CredentialField(string prompt, bool echo) : ObservableObject
{
    string value = "";
    public string Prompt { get; } = prompt;
    public char Mask { get; } = echo ? '\0' : '•';
    public string Value { get => value; set => Set(ref this.value, value); }
}

public sealed class CredentialDialogModel(CredentialChallenge challenge, string endpoint)
{
    public string Title { get; } = challenge.Round > 1 ? $"交互认证（第 {challenge.Round} 轮）" : "登录认证";
    public string Endpoint { get; } = endpoint;
    public string Instruction { get; } = challenge.Instruction.Trim();
    public bool HasInstruction => Instruction.Length > 0;
    public ObservableCollection<CredentialField> Fields { get; } =
        new(challenge.Questions.Select(q => new CredentialField(q.Text.Trim().TrimEnd(':', '：'), q.Echo)));
}

/// <summary>Password and keyboard-interactive prompts (bastion one-time codes). Answers are
/// handed to the caller once and the fields are cleared when the dialog closes.</summary>
public sealed partial class CredentialDialog : Window
{
    string[]? result;

    public CredentialDialog()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("OkButton")!.Click += (_, _) => Finish(true);
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Finish(false);
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Finish(false);
        this.FindControl<Grid>("Header")!.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(false); };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => this.GetVisualDescendantsOfType<TextBox>().FirstOrDefault()?.Focus());
        Closed += (_, _) => { if (DataContext is CredentialDialogModel model) foreach (var field in model.Fields) field.Value = ""; };
    }

    void Finish(bool ok)
    {
        if (ok && DataContext is CredentialDialogModel model) result = model.Fields.Select(f => f.Value).ToArray();
        Close();
    }

    public static async Task<string[]?> AskAsync(Window? owner, CredentialChallenge challenge, string endpoint, CancellationToken ct)
    {
        var dialog = new CredentialDialog { DataContext = new CredentialDialogModel(challenge, endpoint) };
        await DialogHost.ShowAsync(dialog, owner, ct);
        return dialog.result;
    }
}

internal static class DialogHost
{
    /// <summary>Modal over a visible owner; otherwise a normal window. Cancellation closes it.</summary>
    public static async Task ShowAsync(Window dialog, Window? owner, CancellationToken ct)
    {
        using var registration = ct.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        if (ct.IsCancellationRequested) return;
        dialog.Opened += (_, _) => Services.Motion.Open(dialog);
        if (owner is { IsVisible: true })
        {
            await dialog.ShowDialog(owner);
            return;
        }
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Show();
        await closed.Task;
    }

    public static IEnumerable<T> GetVisualDescendantsOfType<T>(this Avalonia.Visual root) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
