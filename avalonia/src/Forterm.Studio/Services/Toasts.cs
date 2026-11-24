using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Studio.Mvvm;

namespace Forterm.Studio.Services;

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>A short message in the corner of the window, with at most one thing to do about it.</summary>
public sealed record ToastMessage(ToastKind Kind, string Text, string? ActionTitle = null, Action? Action = null);

public sealed class ToastItem : ObservableObject
{
    readonly ToastCenter owner;
    readonly DispatcherTimer timer;
    bool leaving;

    internal ToastItem(ToastCenter owner, ToastMessage message, TimeSpan shown)
    {
        this.owner = owner;
        Message = message;
        DismissCommand = new RelayCommand(Dismiss);
        ActionCommand = new RelayCommand(() => { Dismiss(); message.Action?.Invoke(); });
        timer = new DispatcherTimer(shown, DispatcherPriority.Background, (_, _) => Dismiss());
        timer.Start();
    }

    public ToastMessage Message { get; }
    public string Text => Message.Text;
    public string? ActionTitle => Message.ActionTitle;
    public bool HasAction => Message is { ActionTitle: not null, Action: not null };
    public bool IsInfo => Message.Kind == ToastKind.Info;
    public bool IsSuccess => Message.Kind == ToastKind.Success;
    public bool IsWarning => Message.Kind == ToastKind.Warning;
    public bool IsError => Message.Kind == ToastKind.Error;
    /// <summary>Going away: the view fades it out before it is removed.</summary>
    public bool IsLeaving { get => leaving; private set => Set(ref leaving, value); }
    public ICommand DismissCommand { get; }
    public ICommand ActionCommand { get; }

    /// <summary>A message being read stays.</summary>
    public void Hold(bool held) { if (held) timer.Stop(); else if (!leaving) timer.Start(); }

    public void Dismiss()
    {
        timer.Stop();
        if (leaving) return;
        IsLeaving = true;
        if (Motion.Enabled) DispatcherTimer.RunOnce(() => owner.Items.Remove(this), TimeSpan.FromMilliseconds(180));
        else owner.Items.Remove(this);
    }
}

/// <summary>The messages of one window, newest last. UI thread only.</summary>
public sealed class ToastCenter
{
    const int Most = 4;
    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(ToastMessage message)
    {
        // The same message again is the same message: it starts over instead of piling up. One with something to
        // do about it is replaced, because what to do may be another thing.
        if (Items.FirstOrDefault(i => !i.IsLeaving && i.Message.Text == message.Text && i.Message.Kind == message.Kind) is { } same)
        {
            if (message.Action == null && same.Message.Action == null) { same.Hold(true); same.Hold(false); return; }
            same.Dismiss();
        }
        foreach (var old in Items.Where(i => !i.IsLeaving).Take(Math.Max(0, Items.Count(i => !i.IsLeaving) - Most + 1)).ToList()) old.Dismiss();
        Items.Add(new ToastItem(this, message, TimeSpan.FromSeconds(message.Kind is ToastKind.Error or ToastKind.Warning || message.Action != null ? 8 : 4)));
    }
}
