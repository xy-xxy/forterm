using System;
using System.Threading;
using System.Threading.Tasks;
namespace Renci.SshNet.Sftp
{
    public sealed partial class SftpFileStream
    {
        // Forterm: preserve metadata through the already-open staging handle,
        // never by resolving a potentially replaced path for chmod.
        public Task<SftpFileAttributes> ReadHandleAttributesAsync(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _session.RequestFStatAsync(Handle, ct);
        }
        public Task SetHandlePermissionsAsync(short octalDigits, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            var attrs = new SftpFileAttributes(default, default, 0, 0, 0, 0, null);
            attrs.SetPermissions(octalDigits);
            return Task.Run(() => _session.RequestFSetStat(Handle, attrs), CancellationToken.None).WaitAsync(ct);
        }
    }
}
