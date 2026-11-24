using System.Net;
using System.Net.Sockets;
using System.Text;
using Forterm.Core;
using Renci.SshNet;
using Xunit;
namespace Forterm.Tests;
public sealed class RelaySecurityTests
{
 [Fact]public async Task PrivateJumpRelayRejectsNoAuthAndDifferentDestinationBeforeOpeningSshChannel()
 {
  using var jumpServer=new TransportTests.Fixture(forward:true);using var targetServer=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));using var client=new SshClient("127.0.0.1",jumpServer.Port,"synthetic","synthetic-only");client.HostKeyReceived+=(_,e)=>e.CanTrust=true;await client.ConnectAsync(ct.Token);
  using var relay=new JumpProxy(client,"127.0.0.1",targetServer.Port);
  using(var unauthorized=new TcpClient()){await unauthorized.ConnectAsync(IPAddress.Loopback,relay.Port,ct.Token);var stream=unauthorized.GetStream();await stream.WriteAsync(new byte[]{5,1,0},ct.Token);byte[] denied=new byte[2];await stream.ReadExactlyAsync(denied,ct.Token);Assert.Equal(new byte[]{5,255},denied);}
  using(var wrongTarget=new TcpClient()){await wrongTarget.ConnectAsync(IPAddress.Loopback,relay.Port,ct.Token);var stream=wrongTarget.GetStream();await stream.WriteAsync(new byte[]{5,1,2},ct.Token);byte[] response=new byte[2];await stream.ReadExactlyAsync(response,ct.Token);Assert.Equal(new byte[]{5,2},response);byte[] user=Encoding.ASCII.GetBytes(relay.Username),password=Encoding.ASCII.GetBytes(relay.Password);await stream.WriteAsync(new byte[]{1,(byte)user.Length}.Concat(user).Concat(new byte[]{(byte)password.Length}).Concat(password).ToArray(),ct.Token);await stream.ReadExactlyAsync(response,ct.Token);Assert.Equal(new byte[]{1,0},response);
   int wrongPort=targetServer.Port==65535?1:targetServer.Port+1;await stream.WriteAsync(new byte[]{5,1,0,1,127,0,0,1,(byte)(wrongPort>>8),(byte)wrongPort},ct.Token);byte[] rejected=new byte[10];await stream.ReadExactlyAsync(rejected,ct.Token);Assert.Equal(2,rejected[1]);
  }
  Assert.Equal(0,jumpServer.Stats().GetProperty("forwarded").GetInt32());Assert.Equal(0,targetServer.Stats().GetProperty("connections").GetInt32());Assert.True(client.IsConnected);
 }
}
