using System.Text.Json;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class LaunchTests
{
 [Fact]public void OpaqueCaseSensitiveValuesAreDecodedExactlyOnceAndRedacted(){var p=LaunchPlan.Parse(new[]{"-NEWTAB","private title","-URL","SSH://User%2BName:p%2525%40Word@[::1]:2222"},"xftp.exe");Assert.Equal("",p.Error);var c=Assert.IsType<LaunchConnection>(p.Connection);Assert.Equal("sftp",c.Protocol);Assert.Equal("User+Name",c.User);Assert.Equal("p%25@Word",c.Password);Assert.Equal(2222,c.Port);Assert.Equal("private title",c.Title);string report=p.ToString();foreach(var s in new[]{"private title","User+Name","p%25@Word","::1"})Assert.DoesNotContain(s,report);}
 [Theory][InlineData("-url")][InlineData("-unknown")][InlineData("ssh://x:y@host/path")][InlineData("ssh://x:%zz@host")][InlineData("ssh://x:y@host?query")][InlineData("ssh://x:y@host:70000")]
 public void MalformedNeverGetsConnection(string arg){var p=LaunchPlan.Parse(new[]{arg});Assert.NotEmpty(p.Error);Assert.Null(p.Connection);}
 [Fact]public void BurstGatePreservesDifferentTicketsEmptyAndAbsentAndExplicitNewWindow(){using var g=new LaunchGate();var p=LaunchPlan.Parse(new[]{"ssh://u:p@localhost"},"xshell");for(int i=0;i<11;i++)Assert.Equal(i==0,g.Accept(p,TimeSpan.FromMilliseconds(i)));p.Connection!.Password="different";Assert.True(g.Accept(p,TimeSpan.Zero));Assert.True(g.Accept(LaunchPlan.Parse(new[]{"ssh://u:@localhost"},"xshell"),TimeSpan.Zero));Assert.True(g.Accept(LaunchPlan.Parse(new[]{"ssh://u@localhost"},"xshell"),TimeSpan.Zero));Assert.True(g.Accept(LaunchPlan.Parse(new[]{"-newwin","-url","ssh://u:p@localhost"},"xshell"),TimeSpan.Zero));}
 #pragma warning disable xUnit1031 // Named Mutex ownership must stay on this synchronous test thread.
 [Fact]public void PipeTransfersExactArgumentsWithoutPersisting(){string name="forterm-test-"+Guid.NewGuid().ToString("N");var received=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(10));var(primary,forwarded)=InstanceBroker.StartAsync(name,[],args=>received.TrySetResult(args),ct.Token).GetAwaiter().GetResult();Assert.NotNull(primary);try{string[] args=["-newtab","space title","-url","ssh://U:p%2525@localhost:2222"];var forwardedResult=Task.Run(()=>InstanceBroker.StartAsync(name,args,_=>true,ct.Token)).GetAwaiter().GetResult();Assert.True(forwardedResult.Forwarded);Assert.Equal(args,received.Task.WaitAsync(ct.Token).GetAwaiter().GetResult());}finally{primary.Dispose();}}
}
