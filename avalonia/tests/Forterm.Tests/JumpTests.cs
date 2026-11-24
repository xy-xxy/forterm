using System.Text;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class JumpTests
{
 [Fact]public async Task ChildSshAndSftpUseOneAuthenticatedJumpAndRetainItAfterParentViewCloses()
 {
  using var jumpServer=new TransportTests.Fixture(forward:true);using var targetServer=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));
  using var jump=await SharedSshSession.ConnectAsync("127.0.0.1",jumpServer.Port,"synthetic","synthetic-only",(fp,_)=>Task.FromResult(fp==jumpServer.Fingerprint),ct.Token);
  using var target=await SharedSshSession.ConnectAsync("127.0.0.1",targetServer.Port,"synthetic","synthetic-only",(fp,_)=>Task.FromResult(fp==targetServer.Fingerprint),ct.Token,options:new(){Jump=jump});
  using var shell=await target.OpenShellAsync(80,24,ct.Token);using var files=await target.OpenFilesAsync(ct.Token);jump.Dispose();await files.UploadNewAsync(new MemoryStream(new byte[]{4,5,6}),"/through-jump",ct.Token);
  await shell.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("through-jump"),ct.Token);await shell.WriterStream.FlushAsync(ct.Token);var buffer=new byte[256];string output="";while(!output.Contains("through-jump")){int n=await shell.ReaderStream.ReadAsync(buffer,ct.Token);Assert.True(n>0);output+=Encoding.UTF8.GetString(buffer,0,n);}
  Assert.Equal(1,jumpServer.Stats().GetProperty("connections").GetInt32());Assert.Equal(1,jumpServer.Stats().GetProperty("auth").GetInt32());Assert.Equal(1,jumpServer.Stats().GetProperty("forwarded").GetInt32());Assert.Equal(1,targetServer.Stats().GetProperty("auth").GetInt32());Assert.True(jump.IsConnected);
 }
 [Fact]public async Task DeniedDirectChannelDoesNotCloseAuthenticatedJumpSession()
 {
  using var jumpServer=new TransportTests.Fixture();using var targetServer=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  using var jump=await SharedSshSession.ConnectAsync("127.0.0.1",jumpServer.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
  await Assert.ThrowsAnyAsync<Exception>(()=>SharedSshSession.ConnectAsync("127.0.0.1",targetServer.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,options:new(){Jump=jump}));
  using var files=await jump.OpenFilesAsync(ct.Token);await files.UploadNewAsync(new MemoryStream(new byte[]{1}),"/jump-still-usable",ct.Token);Assert.True(jump.IsConnected);Assert.Equal(1,jumpServer.Stats().GetProperty("auth").GetInt32());Assert.Equal(0,targetServer.Stats().GetProperty("connections").GetInt32());
 }
}
