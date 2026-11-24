using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class SshResizeBoundsTests
{
    [Fact] public async Task InvalidGeometryIsIgnoredButOneCellMatchesTheTerminalInsteadOfBecomingTwo()
    {
        using var fixture=new TransportTests.Fixture();using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(15));using var root=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),cts.Token);using var shell=await root.OpenShellAsync(80,24,cts.Token);
        shell.Resize(0,24);shell.Resize(-2,3);shell.Resize(80,0);await Task.Delay(50,cts.Token);Assert.Empty(fixture.Stats().GetProperty("resizes").EnumerateArray());
        shell.Resize(1,1);while(fixture.Stats().GetProperty("resizes").GetArrayLength()==0)await Task.Delay(10,cts.Token);var size=fixture.Stats().GetProperty("resizes").EnumerateArray().Last();Assert.Equal(1,size[0].GetInt32());Assert.Equal(1,size[1].GetInt32());Assert.True(root.IsConnected);using var files=await root.OpenFilesAsync(cts.Token);int count=0;await foreach(var item in files.Client.ListDirectoryAsync("/",cts.Token))count++;Assert.True(count>0);
    }
}
