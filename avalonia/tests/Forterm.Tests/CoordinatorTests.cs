using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class CoordinatorTests
{
 [Fact]public void JumpCyclesAndExcessDepthAreRejectedBeforeAnyAuthentication()
 {
  var a=new ConnectionProfile("a","g","a",22,"u");var b=new ConnectionProfile("b","g","b",22,"u"){JumpId=a.Id};a=a with{JumpId=b.Id};Assert.Throws<IOException>(()=>ConnectionCoordinator.ResolveChain(a,new[]{a,b}));
  var profiles=new List<ConnectionProfile>();string previous="";for(int i=0;i<6;i++){var p=new ConnectionProfile(i.ToString(),"g","host",22,"u"){JumpId=previous};profiles.Add(p);previous=p.Id;}Assert.Throws<IOException>(()=>ConnectionCoordinator.ResolveChain(profiles[^1],profiles));
 }
 [AvaloniaFact]public async Task TwoTargetConnectionsReuseAuthenticatedHiddenJumpWithoutReusingCredentials()
 {
  using var jumpServer=new TransportTests.Fixture(forward:true);using var targetServer=new TransportTests.Fixture();string dir=Path.Combine(Path.GetTempPath(),"forterm-coordinate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var owner=new Window();
  try{
   var parent=new ConnectionProfile("jump","g","127.0.0.1",jumpServer.Port,"synthetic"){HasPassword=true};var target=new ConnectionProfile("target","g","127.0.0.1",targetServer.Port,"synthetic"){JumpId=parent.Id};var store=new ProfileStore(dir);store.Save(new[]{parent,target});var vault=new CredentialStoreTests.MemoryVault();await vault.WriteAsync(ProfileRepository.PasswordKey(parent),System.Text.Encoding.UTF8.GetBytes("synthetic-only"),CancellationToken.None);var repository=new ProfileRepository(store,vault);var coordinator=new ConnectionCoordinator(owner,store,repository,new KnownHosts(dir),()=>new(){KeepAliveEnabled=false},(_,_)=>Task.FromResult(true));using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));
   using var first=await coordinator.ConnectAsync(target,"synthetic-only","",false,ct.Token);using var firstFiles=await first.OpenFilesAsync(ct.Token);using var second=await coordinator.ConnectAsync(target,"synthetic-only","",false,ct.Token);first.Dispose();firstFiles.Dispose();using var secondFiles=await second.OpenFilesAsync(ct.Token);await secondFiles.UploadNewAsync(new MemoryStream(new byte[]{1,2}),"/still-through-jump",ct.Token);Assert.Equal(1,jumpServer.Stats().GetProperty("auth").GetInt32());Assert.Equal(2,jumpServer.Stats().GetProperty("forwarded").GetInt32());Assert.Equal(2,targetServer.Stats().GetProperty("auth").GetInt32());
  }finally{owner.Close();Directory.Delete(dir,true);}
 }
 [AvaloniaFact]public async Task AuthenticationTestCannotReplaceActiveTransportUsedAsJump()
 {
  using var firstServer=new TransportTests.Fixture(forward:true);using var secondServer=new TransportTests.Fixture();string dir=Path.Combine(Path.GetTempPath(),"forterm-test-cache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var owner=new Window();
  try{var firstProfile=new ConnectionProfile("first","g","127.0.0.1",firstServer.Port,"synthetic"){HasPassword=true};var secondProfile=new ConnectionProfile("second","g","127.0.0.1",secondServer.Port,"synthetic"){JumpId=firstProfile.Id};var store=new ProfileStore(dir);store.Save(new[]{firstProfile,secondProfile});var vault=new CredentialStoreTests.MemoryVault();await vault.WriteAsync(ProfileRepository.PasswordKey(firstProfile),System.Text.Encoding.UTF8.GetBytes("synthetic-only"),CancellationToken.None);var coordinator=new ConnectionCoordinator(owner,store,new(store,vault),new(dir),()=>new(){KeepAliveEnabled=false},(_,_)=>Task.FromResult(true));using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));using var active=await coordinator.ConnectAsync(firstProfile,"synthetic-only","",false,ct.Token);using(var probe=await coordinator.ConnectAsync(firstProfile,"synthetic-only","",false,ct.Token,rememberHostKeys:false)){}using var second=await coordinator.ConnectAsync(secondProfile,"synthetic-only","",false,ct.Token);Assert.Equal(2,firstServer.Stats().GetProperty("auth").GetInt32());Assert.Equal(1,firstServer.Stats().GetProperty("forwarded").GetInt32());Assert.True(active.IsConnected);
  }finally{owner.Close();Directory.Delete(dir,true);}
 }
 [Fact]public void PreferenceBoundsAndAtomicPersistenceKeepCredentialsOut()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-preferences-"+Guid.NewGuid().ToString("N"));try{var store=new PreferenceStore(dir);var p=new UserPreferences{Appearance="light",TerminalFontSize=18,UiFontSize=16,KeepAliveSeconds=27,SftpProbeEnabled=false};store.Save(p);Assert.Equal(p,store.Load());Assert.Throws<ArgumentOutOfRangeException>(()=>store.Save(p with{KeepAliveSeconds=4}));Assert.Equal(p,store.Load());Assert.Single(Directory.GetFiles(dir));}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}
