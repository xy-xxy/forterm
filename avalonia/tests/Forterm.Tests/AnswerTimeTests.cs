using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
/// <summary>The time the user takes to answer a question of a connection (its host key, a login prompt): with
/// <see cref="SessionConnectOptions.AnswersUntimed"/> it is not counted, neither by the library's own wait nor by the overall
/// authentication window; without it (Forterm.Desktop) the answer must come in time, as before.</summary>
[Collection("AuthenticationFloor")]
public sealed class AnswerTimeTests
{
 static readonly TimeSpan Slow=TimeSpan.FromMilliseconds(6500);
 static SessionConnectOptions Options(bool untimed)=>new(){ConnectTimeoutSeconds=5,AnswersUntimed=untimed,KeepAliveEnabled=false};
 static async Task<T> Floor<T>(Func<Task<T>> run){int was=SharedSshSession.AuthenticationFloorSeconds;SharedSshSession.AuthenticationFloorSeconds=5;try{return await run();}finally{SharedSshSession.AuthenticationFloorSeconds=was;}}
 [Theory][InlineData(true)][InlineData(false)]
 public async Task AHostKeyAnsweredSlowly(bool untimed)
 {
  using var fixture=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(40));int asked=0;
  Task<SharedSshSession> Connect()=>SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",async(_,_)=>{asked++;await Task.Delay(Slow);return true;},ct.Token,options:Options(untimed));
  if(untimed){using var session=await Floor(Connect);Assert.True(session.IsConnected);}
  else await Assert.ThrowsAnyAsync<Exception>(()=>Floor(Connect));
  Assert.Equal(1,asked);
 }
 [Theory][InlineData(true)][InlineData(false)]
 public async Task ALoginPromptAnsweredSlowly(bool untimed)
 {
  using var fixture=new TransportTests.Fixture(authMode:"partial");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(40));int prompts=0;
  Task<SharedSshSession> Connect()=>SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,false,async(q,_)=>{prompts++;await Task.Delay(Slow);return new[]{"123456"};},options:Options(untimed));
  if(untimed){using var session=await Floor(Connect);Assert.True(session.IsConnected);}
  else await Assert.ThrowsAnyAsync<Exception>(()=>Floor(Connect));
  Assert.Equal(1,prompts);
 }
 /// <summary>The window stops while a question is open and goes on afterwards with what was left: an answer does not
 /// end the limit for good.</summary>
 [Fact]
 public async Task TheWindowGoesOnAfterAnAnswer()
 {
  using var timeout=new CancellationTokenSource();timeout.CancelAfter(TimeSpan.FromMilliseconds(600));
  var clock=new SharedSshSession.AnswerClock(timeout,TimeSpan.FromMilliseconds(600));
  var ask=clock.Wrap<string,bool>(async(_,_)=>{Assert.True(clock.IsAnswering);await Task.Delay(1200);return true;});
  var watch=System.Diagnostics.Stopwatch.StartNew();
  Assert.True(await ask("q",CancellationToken.None));
  Assert.False(timeout.IsCancellationRequested);Assert.False(clock.IsAnswering);
  // About 600 ms were left; it ends then, not never.
  try{await Task.Delay(TimeSpan.FromSeconds(10),timeout.Token);}catch(OperationCanceledException){}
  Assert.True(timeout.IsCancellationRequested);
  Assert.InRange(watch.Elapsed.TotalMilliseconds,1700,5000);
 }
 /// <summary>Time that is not an answer still counts: a server that names itself and then says nothing more (the key
 /// exchange never ends) is given up on in time.</summary>
 [Fact]
 public async Task WaitingForTheServerStillCounts()
 {
  using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
  var serve=Task.Run(async()=>{using var client=await listener.AcceptTcpClientAsync();var stream=client.GetStream();await stream.WriteAsync("SSH-2.0-Silent_1.0\r\n"u8.ToArray());var sink=new byte[4096];try{while(await stream.ReadAsync(sink)>0){}}catch(IOException){}});
  using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(40));var watch=System.Diagnostics.Stopwatch.StartNew();
  await Assert.ThrowsAnyAsync<Exception>(()=>Floor(()=>SharedSshSession.ConnectAsync("127.0.0.1",port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,options:Options(true))));
  Assert.True(watch.Elapsed<TimeSpan.FromSeconds(15),watch.Elapsed.ToString());
  listener.Stop();
 }
}
