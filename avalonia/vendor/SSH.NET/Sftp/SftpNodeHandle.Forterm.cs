using System;
using System.Threading;
using System.Threading.Tasks;
namespace Renci.SshNet.Sftp
{
    public sealed class SftpNodeHandle : IAsyncDisposable
    {
        readonly ISftpSession session; readonly byte[] handle; int disposed;
        internal SftpNodeHandle(ISftpSession session, byte[] handle) { this.session = session; this.handle = handle; }
        public Task<SftpFileAttributes> AttributesAsync(CancellationToken ct) { ObjectDisposedException.ThrowIf(disposed != 0, this); return session.RequestFStatAsync(handle, ct); }
        public Task SetPermissionsAsync(short octalDigits, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this); ct.ThrowIfCancellationRequested();
            var attrs = new SftpFileAttributes(default, default, 0, 0, 0, 0, null); attrs.SetPermissions(octalDigits);
            return Task.Run(() => session.RequestFSetStat(handle, attrs), CancellationToken.None).WaitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await session.RequestCloseAsync(handle, timeout.Token);
        }
    }
}
