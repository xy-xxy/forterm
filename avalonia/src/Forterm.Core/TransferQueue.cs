using System.Threading.Channels;
namespace Forterm.Core;

public enum TransferState { Queued, Running, Verifying, Committing, Completed, Cancelled, Failed, Uncertain }
public sealed record TransferProgress(long Bytes, long? Total, string Phase, string Unit = "B");
public sealed record TransferSnapshot(Guid Id, string Name, string Source, TransferState State, long Bytes, long? Total, string Message, string Unit = "B");
public sealed class TransferTicket
{
    internal readonly CancellationTokenSource CancelSource = new(); internal Func<IProgress<TransferProgress>, CancellationToken, Task>? Execute; internal TransferSnapshot Value; internal CancellationTokenRegistration BoundSource;
    internal TransferTicket(string name, string source, Func<IProgress<TransferProgress>, CancellationToken, Task> run) { Execute = run; Value = new(Guid.NewGuid(), name, source, TransferState.Queued, 0, null, "等待传输"); }
    readonly TaskCompletionSource<TransferSnapshot> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<TransferSnapshot> Completion => completed.Task;
    internal void Finish() => completed.TrySetResult(Value);
    public Guid Id => Value.Id;
    public void Cancel() { if (Value.State is TransferState.Queued or TransferState.Running or TransferState.Verifying) CancelSource.Cancel(); }
}
public sealed class TransferQueue : IDisposable
{
    readonly Channel<TransferTicket> queue = Channel.CreateBounded<TransferTicket>(128); readonly CancellationTokenSource lifetime = new(); readonly object gate = new(); readonly List<TransferTicket> items = new(); readonly Task worker; long lastNotice;
    public event Action? Changed;
    public TransferQueue() => worker = Task.Run(Work);
    public IReadOnlyList<TransferSnapshot> Snapshot() { lock (gate) return items.Select(t => t.Value).ToArray(); }
    public TransferTicket Add(string name, string source, Func<IProgress<TransferProgress>, CancellationToken, Task> run, CancellationToken sourceClosed = default)
    {
        var ticket = new TransferTicket(name, source, run); bool accepted;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(lifetime.IsCancellationRequested, this);
            if (items.Count >= 512) items.RemoveAll(t => t.Value.State is TransferState.Completed or TransferState.Cancelled);
            if (items.Count >= 512) throw new IOException("传输队列已满，请清理已结束任务"); items.Add(ticket);
            if (sourceClosed.CanBeCanceled) ticket.BoundSource = sourceClosed.Register(() => Cancel(ticket.Id));
            accepted = queue.Writer.TryWrite(ticket); if (!accepted) items.Remove(ticket);
        }
        if (!accepted) { ticket.BoundSource.Dispose(); throw new IOException("等待队列已满"); }
        Changed?.Invoke(); return ticket;
    }
    public void Cancel(Guid id) { lock (gate) { var t = items.FirstOrDefault(t => t.Id == id); if (t == null || t.Value.State is not (TransferState.Queued or TransferState.Running or TransferState.Verifying)) return; t.Cancel(); t.Value = t.Value with { State = t.Value.State == TransferState.Queued ? TransferState.Cancelled : t.Value.State, Message = "正在取消；等待临时项清理" }; if (t.Value.State == TransferState.Cancelled) { t.Execute = null; t.Finish(); } } Changed?.Invoke(); }
    void Set(TransferTicket t, TransferState state, string message) { lock (gate) t.Value = t.Value with { State = state, Message = message }; if (state is TransferState.Completed or TransferState.Cancelled or TransferState.Failed or TransferState.Uncertain) t.Finish(); Changed?.Invoke(); }
    async Task Work()
    {
        try
        {
            await foreach (var t in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, t.CancelSource.Token);
                Func<IProgress<TransferProgress>, CancellationToken, Task>? execute;
                lock (gate) { execute = linked.IsCancellationRequested ? null : t.Execute; if (execute != null) t.Value = t.Value with { State = TransferState.Running, Message = "传输中" }; }
                if (execute == null) { Set(t, TransferState.Cancelled, "排队任务已取消"); t.Execute = null; t.BoundSource.Dispose(); continue; }
                Changed?.Invoke();
                try { await execute(new InlineProgress(p => { lock (gate) t.Value = t.Value with { Bytes = p.Bytes, Total = p.Total, Unit = p.Unit, State = p.Phase == "提交中" ? TransferState.Committing : p.Phase.Contains("校验") ? TransferState.Verifying : TransferState.Running, Message = p.Phase }; long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (p.Phase != "传输中" || now - Interlocked.Read(ref lastNotice) > System.Diagnostics.Stopwatch.Frequency / 10) { Interlocked.Exchange(ref lastNotice, now); Changed?.Invoke(); } }), linked.Token); Set(t, TransferState.Completed, "已完成并确认提交"); }
                catch (CommitUncertainException e) { Set(t, TransferState.Uncertain, e.Message); }
                catch (OperationCanceledException) { Set(t, TransferState.Cancelled, "已取消；未提交目标，临时项清理见详情"); }
                catch (Exception e) { Set(t, TransferState.Failed, e.Message); }
                finally { t.Execute = null; t.BoundSource.Dispose(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    sealed class InlineProgress(Action<TransferProgress> report) : IProgress<TransferProgress> { public void Report(TransferProgress value) => report(value); }
    public void ClearFinished() { lock (gate) items.RemoveAll(t => t.Value.State is TransferState.Completed or TransferState.Cancelled or TransferState.Failed); Changed?.Invoke(); }
    public void Dispose() { lifetime.Cancel(); queue.Writer.TryComplete(); TransferTicket[] copy; lock (gate) { copy = items.ToArray(); foreach (var t in copy) { t.CancelSource.Cancel(); if (t.Value.State == TransferState.Queued) { t.Value = t.Value with { State = TransferState.Cancelled, Message = "工作区已关闭" }; t.Execute = null; t.Finish(); } } } foreach (var t in copy) t.BoundSource.Dispose(); Changed?.Invoke(); }
}
public sealed class CommitUncertainException(string message, Exception? inner = null) : IOException(message, inner);
