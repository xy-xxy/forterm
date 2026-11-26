using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Studio.Controls;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;
using Porta.Pty;

namespace Forterm.Studio.ViewModels;

/// <summary>A tab in the centre column: a terminal or a remote text editor.</summary>
public abstract class CenterTabViewModel : ObservableObject, IDisposable
{
    bool active;
    public abstract string Title { get; }
    /// <summary>What the centre column shows for this tab.</summary>
    public abstract object Content { get; }
    public bool IsActive { get => active; set => Set(ref active, value); }
    /// <summary>"会话记录" is writing this terminal's output to a file.</summary>
    public virtual bool IsRecording => false;
    public ICommand? ActivateCommand { get; set; }
    public ICommand? CloseCommand { get; set; }
    public abstract void Dispose();
}

/// <summary>One terminal tab of a workspace. SSH tabs are separate shell channels on the
/// workspace's single authenticated transport.</summary>
public sealed class TerminalTabViewModel(string title, StudioTerminalControl control) : CenterTabViewModel
{
    IPtyConnection? connection;
    SessionRecorder? recorder;

    public override string Title { get; } = title;
    public StudioTerminalControl Control { get; } = control;
    public override object Content => Control;
    public bool HasConnection => connection != null;
    public IDisposable? Owned { get; set; }
    /// <summary>Last directory the shell reported (pwd follow), if any.</summary>
    public string? Directory { get; set; }

    /// <param name="direct">The connection itself, below what <paramref name="attached"/> adds to it.</param>
    public void Attach(IPtyConnection attached, Core.IImmediateInput? direct = null)
    {
        Idle = new IdleKeeper(Time);
        watch = new IdleWatch(attached, Idle, direct);
        connection = watch;
        Control.AttachWhenReady(watch);
        Raise(nameof(HasConnection));
    }

    internal TimeProvider Time { get; set; } = TimeProvider.System;
    /// <summary>Starts counting anew, with another clock.</summary>
    internal void UseClockForTest(TimeProvider clock) { Time = clock; Idle = new IdleKeeper(clock); watch?.Use(Idle); }
    IdleWatch? watch;
    bool failed;
    long printed = -1;

    /// <summary>Types into the terminal, through any encoding conversion and in turn with everything else that is sent
    /// from here. <paramref name="byUser"/>: not what the settings of the connection write.</summary>
    public Task WriteAsync(string text, bool byUser, CancellationToken ct) =>
        watch?.SendAsync(System.Text.Encoding.UTF8.GetBytes(text), byUser, ct) ?? Task.CompletedTask;

    CancellationTokenSource? paste;
    bool pasteSlow;

    /// <summary>A paste is still going out: the server has not taken all of it.</summary>
    public bool IsPasting => paste != null;
    /// <summary>It has been going out for a while: time to offer to stop it.</summary>
    public bool IsPasteSlow => pasteSlow;
    /// <summary>How long a paste goes out before it can be stopped from the window.</summary>
    internal static TimeSpan SlowPaste { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>Stops what of the paste has not gone out yet. The bar goes at once; a bracket that was opened still has to
    /// be closed, when the server takes it.</summary>
    public bool CancelPaste()
    {
        if (paste is not { IsCancellationRequested: false } current) return false;
        current.Cancel();
        if (pasteSlow) { pasteSlow = false; Raise(nameof(IsPasteSlow)); }
        return true;
    }

    /// <summary>Sends a paste the user agreed to (see <see cref="IdleWatch.SendPasteAsync"/>). One at a time: null when
    /// another is still going out, or there is no shell. False = stopped by the user.</summary>
    public async Task<bool?> PasteAsync(string head, string text, bool bracketed, CancellationToken lifetime)
    {
        if (watch is not { } to || paste != null) return null;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        paste = cancel;
        Raise(nameof(IsPasting));
        _ = ShowSlowAsync(cancel);
        try { return await to.SendPasteAsync(head, text, bracketed, cancel.Token, lifetime); }
        finally
        {
            paste = null;
            pasteSlow = false;
            Raise(nameof(IsPasting));
            Raise(nameof(IsPasteSlow));
        }
    }

    async Task ShowSlowAsync(CancellationTokenSource which)
    {
        await Task.Delay(SlowPaste);
        if (!ReferenceEquals(paste, which) || which.IsCancellationRequested || pasteSlow) return;
        pasteSlow = true;
        Raise(nameof(IsPasteSlow));
    }

    /// <summary>"空闲保活" of this terminal; null without a connection.</summary>
    public IdleKeeper? Idle { get; private set; }
    /// <summary>There is something to keep: the shell has not ended.</summary>
    public bool IsIdleKept => watch is { IsEnded: false } && !failed;
    /// <summary>Why "空闲保活" sends nothing although it is time; "" when it does not hold back.</summary>
    public string IdleHeld { get; private set; } = "";

    /// <summary>Sends what "空闲保活" says, when it is due. After an error it has ended for this shell.</summary>
    public async Task<bool> KeepAliveWhenIdleAsync(StudioPreferences preferences, CancellationToken ct)
    {
        if (watch is not { } to || Idle is not { } idle || !IsIdleKept) { IdleHeld = ""; return false; }
        // Where something is being printed there is no empty prompt, whatever the line looks like just now.
        long seen = printed;
        IdleDue Now()
        {
            var place = Control.ReadPlace();
            printed = place.Output;
            return idle.Due(preferences, place.FullScreen, place.AtPrompt && place.Output == seen);
        }
        var due = Now();
        IdleHeld = due.Held;
        if (due.Text == null) return false;
        try
        {
            if (await to.BeatAsync(() => Now().Text, ct)) return true;
            if (IsIdleKept && Now().Text != null) IdleHeld = "服务器没有在接收输入";
            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException) { failed = true; throw; }
    }

    public override bool IsRecording => recorder != null;
    public string RecordingPath => recorder?.Path ?? "";

    /// <summary>Records this terminal's output until <see cref="StopRecording"/>, an error or the tab's end.
    /// <paramref name="stopped"/> is told (on the UI thread) when recording ended by itself.</summary>
    public void StartRecording(string path, string header, Action<string> stopped)
    {
        if (recorder != null) return;
        SessionRecorder? started = null;
        started = SessionRecorder.Start(Control, path, header, reason => Dispatcher.UIThread.Post(() =>
        {
            // Also when it was being stopped: what could not be written is worth saying.
            if (ReferenceEquals(recorder, started)) { recorder = null; Raise(nameof(IsRecording)); }
            stopped(reason);
        }));
        recorder = started;
        Raise(nameof(IsRecording));
    }

    /// <summary>First step of closing several terminals: their recordings finish side by side.</summary>
    public void EndRecording() => recorder?.End();

    /// <summary>False when the last output is still being written (a slow disk).</summary>
    public bool StopRecording()
    {
        if (recorder is not { } current) return true;
        recorder = null;
        bool finished = current.Stop();
        Raise(nameof(IsRecording));
        return finished;
    }

    public override void Dispose()
    {
        StopRecording();
        Control.EndReparent();
        Control.Dispose();
        Owned?.Dispose();
        connection = null;
        watch = null;
        Idle = null;
    }
}
