using System.Diagnostics;
using System.Text.Json;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class AgentTests
{
 [Theory][InlineData("rsa")][InlineData("ecdsa")][InlineData("ed25519")]public async Task OwnedAgentSignsRealSshAuthenticationWithoutReadingPrivateKeyInClient(string keyAlgorithm)
 {
  if(OperatingSystem.IsWindows())return;using var server=new TransportTests.Fixture(authMode:"key",keyAlgorithm:keyAlgorithm);var root=TransportTests.FindRoot();var start=new ProcessStartInfo(Path.Combine(root,"tooling/fixture-venv/bin/python3")){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.Combine(root,"tooling/agent_fixture.py"));start.ArgumentList.Add(server.PrivateKeyPath);using var peer=Process.Start(start)!;
  try{
   string line=(await peer.StandardOutput.ReadLineAsync())!;var info=JsonDocument.Parse(line).RootElement;string socket=info.GetProperty("socket").GetString()!,fingerprint=info.GetProperty("fingerprint").GetString()!;using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));
   using var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","must-not-use-password",(_,_)=>Task.FromResult(true),ct.Token,true,null,"agent",options:new(){AgentProvider=t=>OpenSshAgent.ConnectUnixAsync(socket,t),AgentFingerprint=fingerprint});
   using var files=await session.OpenFilesAsync(ct.Token);Assert.True(session.IsConnected);Assert.Equal(0,server.Stats().GetProperty("passwordRequests").GetInt32());peer.StandardInput.WriteLine("stats");peer.StandardInput.Flush();var stats=JsonDocument.Parse((await peer.StandardOutput.ReadLineAsync())!).RootElement;Assert.Equal(1,stats.GetProperty("identities").GetInt32());Assert.True(stats.GetProperty("signs").GetInt32()>=1);if(keyAlgorithm=="rsa")Assert.Contains(stats.GetProperty("flags").EnumerateArray(),x=>x.GetInt32()==4);else Assert.All(stats.GetProperty("flags").EnumerateArray(),x=>Assert.Equal(0,x.GetInt32()));
  }finally{if(!peer.HasExited){peer.StandardInput.WriteLine("stop");peer.StandardInput.Flush();if(!peer.WaitForExit(2000))peer.Kill(true);}}
 }
 [Fact]public async Task AgentCannotSilentlyDowngradeRsaSha2SignatureToSha1()
 {
  if(OperatingSystem.IsWindows())return;using var server=new TransportTests.Fixture(authMode:"key");var root=TransportTests.FindRoot();var start=new ProcessStartInfo(Path.Combine(root,"tooling/fixture-venv/bin/python3")){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.Combine(root,"tooling/agent_fixture.py"));start.ArgumentList.Add(server.PrivateKeyPath);start.Environment["FIXTURE_AGENT_BAD_ALGO"]="1";using var peer=Process.Start(start)!;using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));
  try{var info=JsonDocument.Parse((await peer.StandardOutput.ReadLineAsync(ct.Token))!).RootElement;string socket=info.GetProperty("socket").GetString()!;var error=await Assert.ThrowsAnyAsync<IOException>(()=>SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","must-not-use-password",(_,_)=>Task.FromResult(true),ct.Token,true,null,"agent",options:new(){AgentProvider=t=>OpenSshAgent.ConnectUnixAsync(socket,t)}));Assert.Contains("不同签名算法",error.Message);Assert.Equal(0,server.Stats().GetProperty("passwordRequests").GetInt32());}
  finally{if(!peer.HasExited){peer.StandardInput.WriteLine("stop");peer.StandardInput.Flush();if(!peer.WaitForExit(2000))peer.Kill(true);}}
 }
 [Fact]public async Task OversizedAgentResponseIsRejectedBeforePayloadAllocation()
 {
  using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(5));using var agent=new OpenSshAgent(new ScriptedStream(new byte[]{0,4,0,1}),ct.Token);await Assert.ThrowsAsync<IOException>(()=>agent.IdentitiesAsync(ct.Token));
 }
 sealed class ScriptedStream(byte[] response):Stream
 {
  readonly MemoryStream input=new(response);public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override int Read(byte[] b,int offset,int count)=>input.Read(b,offset,count);public override ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default)=>input.ReadAsync(b,ct);public override void Write(byte[] b,int offset,int count){}public override ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken ct=default)=>ValueTask.CompletedTask;public override void Flush(){}public override long Seek(long p,SeekOrigin o)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
 }

}
