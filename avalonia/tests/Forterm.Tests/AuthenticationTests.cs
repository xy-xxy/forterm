using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class AuthenticationTests
{
 [Theory][InlineData(false,0)][InlineData(true,1)]
 public async Task OnlyKeyboardPasswordUsesManualInputOnceButNeverAutoReplaysTicket(bool temporary,int expectedPrompts)
 {
  using var fixture=new TransportTests.Fixture(authMode:"keyboard");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int prompts=0;
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,temporary,(q,_)=>{prompts++;Assert.Equal(temporary,q.Temporary);Assert.False(q.PasswordAlreadySubmitted);return Task.FromResult<string[]?>(new[]{"synthetic-only"});});
  Assert.True(session.IsConnected);Assert.Equal(expectedPrompts,prompts);
 }
 [Fact]public async Task PasswordPartialSuccessRequiresExplicitSecondFactor()
 {
  using var fixture=new TransportTests.Fixture(authMode:"partial");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int prompts=0;
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,false,(q,_)=>{prompts++;Assert.True(q.PasswordAlreadySubmitted);Assert.Contains("OTP",q.Questions[0].Text);return Task.FromResult<string[]?>(new[]{"123456"});});Assert.Equal(1,prompts);Assert.True(session.IsConnected);
 }
 [Fact]public async Task MultiRoundKeyboardPromptsOnlyForNextFactorForManualConnection()
 {
  using var fixture=new TransportTests.Fixture(authMode:"multi");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int prompts=0;
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,false,(q,_)=>{prompts++;Assert.Equal(2,q.Round);Assert.Contains("OTP",q.Questions[0].Text);return Task.FromResult<string[]?>(new[]{"123456"});});Assert.Equal(1,prompts);Assert.True(session.IsConnected);
 }
 [Theory][InlineData(false)][InlineData(true)]public async Task RepeatedPasswordRequestAlwaysUsesNewExplicitAnswer(bool temporary)
 {
  using var fixture=new TransportTests.Fixture(authMode:"repeat-password");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int prompts=0;
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,temporary,(q,_)=>{prompts++;Assert.True(q.PasswordAlreadySubmitted);return Task.FromResult<string[]?>(new[]{"synthetic-fresh"});});Assert.Equal(1,prompts);Assert.True(session.IsConnected);Assert.Equal(2,fixture.Stats().GetProperty("auth").GetInt32());
 }
 [Theory][InlineData(false)][InlineData(true)]public async Task ExplicitPrivateKeyUsesOwnedKeyAndNoPasswordFallback(bool encrypted)
 {
  using var fixture=new TransportTests.Fixture(authMode:"key",encryptedKey:encrypted);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","not-a-key-password",(_,_)=>Task.FromResult(true),ct.Token,true,null,"key",fixture.PrivateKeyPath,encrypted?"synthetic-phrase":"");
  using var files=await session.OpenFilesAsync(ct.Token);Assert.True(session.IsConnected);var stats=fixture.Stats();Assert.Equal(1,stats.GetProperty("connections").GetInt32());Assert.Equal(0,stats.GetProperty("passwordRequests").GetInt32());Assert.Equal(2,stats.GetProperty("keyChecks").GetInt32()); // unsigned key probe + signed request, one transport
 }
 [Fact]public async Task RekeyRetainsVerifiedFingerprintAfterInitialAuthenticationTimeoutIsDisposed()
 {
  using var fixture=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int confirmations=0;
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>{confirmations++;return Task.FromResult(true);},ct.Token);
  fixture.Rekey();using var files=await session.OpenFilesAsync(ct.Token);Assert.True(session.IsConnected);Assert.Equal(1,confirmations);Assert.Equal(1,fixture.Stats().GetProperty("auth").GetInt32());
 }
 [Theory][InlineData(false,1)][InlineData(true,2)]public async Task PasswordAfterInteractivePartialSuccessAlwaysRequiresFreshAnswer(bool temporary,int expectedPrompts)
 {
  using var fixture=new TransportTests.Fixture(authMode:"keyboard-then-password");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int prompts=0;
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,temporary,(q,_)=>{prompts++;return Task.FromResult<string[]?>(new[]{q.PasswordAlreadySubmitted?"synthetic-fresh":"synthetic-only"});});Assert.Equal(expectedPrompts,prompts);Assert.True(session.IsConnected);Assert.Equal(1,fixture.Stats().GetProperty("passwordRequests").GetInt32());
 }
 [Fact]public async Task CancelledChallengeDoesNotRetryOrBecomeSuccessful()
 {
  using var fixture=new TransportTests.Fixture(authMode:"keyboard");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));int prompts=0;
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token,true,(q,_)=>{prompts++;return Task.FromResult<string[]?>(null);}));Assert.Equal(1,prompts);
 }
}
