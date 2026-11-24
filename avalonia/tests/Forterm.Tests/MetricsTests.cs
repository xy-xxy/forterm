using System.Text;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;

public sealed class MetricsTests
{
    static string Sample(int n = 0) => $"FT_MONITOR1\nUPTIME\n{100 + n * 3}.0 20\nLOAD\n0.1 0.2 0.3 1/2 3\nCPU\ncpu {100 + n * 10} 0 10 {200 + n * 30} 0 0 0 0 99999 99\ncpu0 {100 + n * 10} 0 10 {200 + n * 30} 0 0 0 0 99999 99\nMEM\nMemTotal: 1024 kB\nMemAvailable: 512 kB\nDISK\nFilesystem 1024-blocks Used Available Capacity Mounted on\nroot 1000 400 600 40% /\nNET\neth0: {1000 + n * 300} 0 0 0 0 0 0 0 {2000 + n * 600} 0 0 0 0 0 0 0\nlo: {n * 999999} 0 0 0 0 0 0 0 {n * 999999} 0 0 0 0 0 0 0\n";
    [Fact] public void FirstSampleHasNoInventedRatesAndGuestIsNotDoubleCounted()
    {
        var first = LinuxMetrics.Parse(Sample()); var a = LinuxMetrics.Compare(first, null);
        Assert.Null(a.CpuPercent["cpu"]); Assert.Null(a.ReceiveBytesPerSecond); Assert.Equal(50, a.MemoryPercent); Assert.Equal(40, a.DiskPercent);
        var b = LinuxMetrics.Compare(LinuxMetrics.Parse(Sample(1)), first);
        Assert.Equal(25, b.CpuPercent["cpu"]); Assert.Equal(25, b.CpuPercent["cpu0"]); Assert.Equal(100, b.ReceiveBytesPerSecond); Assert.Equal(200, b.SendBytesPerSecond);
    }
    [Fact] public void RollbackMissingFieldsAndChangedInterfacesStayUnknown()
    {
        var first = LinuxMetrics.Parse(Sample()); var next = LinuxMetrics.Parse(Sample(1));
        var reset = LinuxMetrics.Compare(first, next); Assert.Null(reset.CpuPercent["cpu"]); Assert.Null(reset.ReceiveBytesPerSecond);
        var rollback = LinuxMetrics.Parse(Sample(1).Replace("cpu 110", "cpu 1")); Assert.Null(LinuxMetrics.Compare(rollback, first).CpuPercent["cpu"]);
        var changed = LinuxMetrics.Parse(Sample(1).Replace("eth0:", "eth1:")); Assert.Null(LinuxMetrics.Compare(changed, first).ReceiveBytesPerSecond);
        var missing = LinuxMetrics.Compare(LinuxMetrics.Parse("FT_MONITOR1\nMEM\nMemTotal: 0 kB\nMemAvailable: 1 kB\nCPU\ncpu nope\n"), first);
        Assert.Null(missing.MemoryPercent); Assert.Null(missing.DiskPercent); Assert.Empty(missing.CpuPercent); Assert.Null(missing.ReceiveBytesPerSecond);
        Assert.Throws<IOException>(() => LinuxMetrics.Parse("not linux"));
    }
    [Fact] public async Task ExecSharesAuthenticationWithShellAndFilesWithoutPtyInput()
    {
        using var fixture = new TransportTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var shell = await root.OpenShellAsync(80, 24, ct.Token); using var files = await root.OpenFilesAsync(ct.Token);
        var read = root.ReadLinuxMetricsAsync(ct.Token); await files.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("text-editor-source")), "/edit.txt", ct.Token);
        var first = await read; var second = await root.ReadLinuxMetricsAsync(ct.Token); Assert.Equal(25, LinuxMetrics.Compare(second, first).CpuPercent["cpu"]);
        var stats = fixture.Stats(); Assert.Equal(1, stats.GetProperty("auth").GetInt32()); Assert.Equal(2, stats.GetProperty("execs").GetInt32()); Assert.Empty(stats.GetProperty("input").EnumerateArray()); Assert.All(stats.GetProperty("commands").EnumerateArray(), x => Assert.Equal(LinuxMetrics.Command, x.GetString()));
    }
    [Theory] [InlineData("FIXTURE_DENY_EXEC")] [InlineData("FIXTURE_EXEC_FLOOD")] [InlineData("FIXTURE_EXEC_SLOW")]
    public async Task ExecDenialOversizeAndTimeoutPreserveOtherChannels(string mode)
    {
        using var fixture = new TransportTests.Fixture(mode); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); using var shell = await root.OpenShellAsync(80, 24, ct.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => root.ReadLinuxMetricsAsync(ct.Token)); Assert.True(root.IsConnected);
        await files.UploadNewAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "/survives-exec", ct.Token);
        await shell.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("alive"), ct.Token); await shell.WriterStream.FlushAsync(ct.Token);
        Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
    [Fact] public async Task ConcurrentRefreshIsRejectedAndCancellationIsChannelLocal()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_EXEC_SLOW"); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct.Token);
        var first = root.ReadLinuxMetricsAsync(request.Token); await Task.Delay(100, ct.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => root.ReadLinuxMetricsAsync(ct.Token)); request.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first); Assert.True(root.IsConnected);
        using var files = await root.OpenFilesAsync(ct.Token); Assert.True(files.IsAvailable); Assert.Equal(1, fixture.Stats().GetProperty("execs").GetInt32());
    }
}
