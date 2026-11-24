using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class HeartbeatTests
{
 [Fact]public async Task GlobalAndOpenedSftpHeartbeatsAreProtocolOnlyAndDisabledSettingStopsBoth()
 {
  using var server=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));
  using(var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,options:new(){KeepAliveSeconds=5})){
   using var shell=await session.OpenShellAsync(80,24,ct.Token);using var files=await session.OpenFilesAsync(ct.Token);await Task.Delay(6200,ct.Token);var stats=server.Stats();Assert.True(stats.GetProperty("globals").GetInt32()>=1);Assert.True(stats.GetProperty("lstats").GetInt32()>=1);Assert.Empty(stats.GetProperty("input").EnumerateArray());Assert.Equal(1,stats.GetProperty("sftp").GetInt32());
  }
  using var disabledServer=new TransportTests.Fixture();using var disabled=await SharedSshSession.ConnectAsync("127.0.0.1",disabledServer.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,options:new(){KeepAliveEnabled=false,KeepAliveSeconds=5});using var disabledFiles=await disabled.OpenFilesAsync(ct.Token);await Task.Delay(6200,ct.Token);var none=disabledServer.Stats();Assert.Equal(0,none.GetProperty("globals").GetInt32());Assert.Equal(0,none.GetProperty("lstats").GetInt32());
 }
 [Fact]public async Task UnansweredGlobalRequestIsNotStackedAndStopsTransportWithoutReauthentication()
 {
  using var server=new TransportTests.Fixture(silentGlobal:true);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(28));using var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,options:new(){KeepAliveSeconds=5,SftpProbeEnabled=false});
  while(session.IsConnected)await Task.Delay(30,ct.Token);Assert.Contains("保活",session.CloseReason);var stats=server.Stats();Assert.Equal(1,stats.GetProperty("globals").GetInt32());Assert.Equal(1,stats.GetProperty("auth").GetInt32());Assert.Empty(stats.GetProperty("input").EnumerateArray());
 }
 [Fact]public async Task SftpProbeTimeoutStopsOnlyFileChannelWhileShellRemainsUsable()
 {
  using var server=new TransportTests.Fixture(statDelay:20);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));using var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,options:new(){KeepAliveSeconds=5});using var shell=await session.OpenShellAsync(80,24,ct.Token);using var files=await session.OpenFilesAsync(ct.Token);
  while(files.IsAvailable)await Task.Delay(30,ct.Token);Assert.Contains("SFTP",files.CloseReason);Assert.True(session.IsConnected);await shell.WriterStream.WriteAsync(new byte[]{65},ct.Token);await shell.WriterStream.FlushAsync(ct.Token);var read=new byte[128];Assert.True(await shell.ReaderStream.ReadAsync(read,ct.Token)>0);Assert.Equal(1,server.Stats().GetProperty("lstats").GetInt32());
 }
}
