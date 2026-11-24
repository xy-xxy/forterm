using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
namespace Forterm.Core;

internal sealed record PermissionExecResult(int? ExitCode, string Output, string Error);
public sealed partial class SharedSshSession
{
    readonly SemaphoreSlim permissionExecGate = new(1, 1);
    internal async Task<PermissionExecResult> PermissionExecAsync(string commandText, string? marker, Func<CancellationToken, Task<byte[]?>>? password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await permissionExecGate.WaitAsync(0, ct)) throw new IOException("上次权限exec仍在收尾，未叠加");
        IDisposable retained;
        try { retained = Retain(); } catch { permissionExecGate.Release(); throw; }
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, transportClosed.Token); timeout.CancelAfter(TimeSpan.FromSeconds(marker == null ? 15 : 90)); var token = timeout.Token;
        var worker = Task.Run(async () =>
        {
            try
            {
                using var command = client.CreateCommand(commandText); command.MaxOutputBytes = 65536; command.CommandTimeout = TimeSpan.FromSeconds(marker == null ? 15 : 90);
                var execution = command.ExecuteAsync(token); Stream? input = null;
                if (marker != null) { try { input = command.CreateInputStream(); } catch (InvalidOperationException) { /* a fast NOPASSWD command may already have exited */ } }
                var output = new StreamReader(command.OutputStream, new UTF8Encoding(false, true)).ReadToEndAsync(token);
                async Task<string> Error()
                {
                    using var reader = new StreamReader(command.ExtendedOutputStream, new UTF8Encoding(false, true)); char[] block = new char[512]; var text = new StringBuilder(); bool answered = false;
                    while (true)
                    {
                        int n = await reader.ReadAsync(block.AsMemory(), token); if (n == 0) break; text.Append(block, 0, n);
                        if (text.Length > 32768) throw new IOException("权限stderr超过32KiB边界");
                        if (!answered && marker != null && text.ToString().Contains(marker, StringComparison.Ordinal))
                        {
                            answered = true;
                            byte[]? secret = password == null ? null : await password(token);
                            try
                            {
                                if (input == null) throw new IOException("sudo输入通道不可用；没有发送密码");
                                if (secret == null) throw new OperationCanceledException("sudo认证已取消", token);
                                if (secret.Length > 8192 || secret.Any(b => b is 0 or 10 or 13)) throw new IOException("sudo密码包含控制字节或超出边界");
                                await input.WriteAsync(secret, token); await input.WriteAsync(new byte[] { 10 }, token); await input.FlushAsync(token);
                            }
                            finally { if (secret != null) CryptographicOperations.ZeroMemory(secret); input?.Dispose(); }
                        }
                    }
                    return text.ToString();
                }
                var error = Error();
                try
                {
                    await execution; var result = new PermissionExecResult(command.ExitStatus, await output, await error); return result;
                }
                finally
                {
                    // Dispose wakes both PipeStream readers even when channel open/exec failed.
                    input?.Dispose(); command.Dispose(); _ = output.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); _ = error.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            finally { timeout.Dispose(); retained.Dispose(); permissionExecGate.Release(); }
        }, CancellationToken.None);
        _ = worker.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        try { return await worker.WaitAsync(token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !transportClosed.IsCancellationRequested) { throw new TimeoutException("权限exec超过截止时间；未叠加请求或重新认证，其他能力保留"); }
    }
}
