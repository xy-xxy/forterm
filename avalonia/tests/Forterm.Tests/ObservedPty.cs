using Porta.Pty;
namespace Forterm.Tests;
internal sealed class ObservedPty(IPtyConnection inner):IPtyConnection
{
    public readonly List<(int Cols,int Rows)> Sizes=[];
    public Stream ReaderStream=>inner.ReaderStream;public Stream WriterStream=>inner.WriterStream;public bool SupportsCancellableRead=>inner.SupportsCancellableRead;public int Pid=>inner.Pid;public int ExitCode=>inner.ExitCode;
    public event EventHandler<PtyExitedEventArgs>? ProcessExited{add=>inner.ProcessExited+=value;remove=>inner.ProcessExited-=value;}
    public void Resize(int cols,int rows){Sizes.Add((cols,rows));inner.Resize(cols,rows);}
    public bool WaitForExit(int ms)=>inner.WaitForExit(ms);public void Kill()=>inner.Kill();public void Dispose()=>inner.Dispose();
}
