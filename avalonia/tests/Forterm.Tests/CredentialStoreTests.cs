using System.Text;
using System.Text.Json;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class CredentialStoreTests
{
 public sealed class MemoryVault:ISecretVault
 {
  public readonly Dictionary<string,byte[]> Values=new();public bool FailWrite;public Action? AfterWrite;public int Reads,Writes,Deletes;
  public Task<byte[]?> ReadAsync(string key,CancellationToken ct){Reads++;return Task.FromResult(Values.TryGetValue(key,out var b)?b.ToArray():null);}
  public Task WriteAsync(string key,byte[] secret,CancellationToken ct){Writes++;if(FailWrite)throw new IOException("synthetic vault failure");Values[key]=secret.ToArray();var action=AfterWrite;AfterWrite=null;action?.Invoke();return Task.CompletedTask;}
  public Task DeleteAsync(string key,CancellationToken ct){Deletes++;Values.Remove(key);return Task.CompletedTask;}
 }
 [Fact]public async Task ExplicitSaveOnlyUsesVaultAndMetadataFailureRestoresPreviousSecret()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-vault-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{
   var store=new ProfileStore(dir);var vault=new MemoryVault();var repo=new ProfileRepository(store,vault);var profile=new ConnectionProfile("name","group","synthetic",22,"user");
   var first=await repo.SaveAsync(null,new(profile,"old-secret","",true,false),CancellationToken.None);Assert.True(first.HasPassword);Assert.Equal("old-secret",await repo.PasswordAsync(first,CancellationToken.None));Assert.DoesNotContain("old-secret",File.ReadAllText(Path.Combine(dir,"profiles.json")));
   string path=Path.Combine(dir,"profiles.json"),before=File.ReadAllText(path);
   vault.AfterWrite=()=>{if(!OperatingSystem.IsWindows())File.SetUnixFileMode(dir,UnixFileMode.UserRead|UnixFileMode.UserExecute);else File.SetAttributes(path,FileAttributes.ReadOnly);};
   try{var error=await Record.ExceptionAsync(()=>repo.SaveAsync(first,new(first,"new-secret","",true,false),CancellationToken.None));Assert.True(error is IOException or UnauthorizedAccessException);}
   finally{if(!OperatingSystem.IsWindows())File.SetUnixFileMode(dir,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);else File.SetAttributes(path,FileAttributes.Normal);}
   Assert.Equal("old-secret",Encoding.UTF8.GetString(vault.Values[ProfileRepository.PasswordKey(first)]));Assert.Equal(before,File.ReadAllText(path));
  }finally{Directory.Delete(dir,true);}
 }
 [Fact]public async Task UncheckedNewPasswordNeverTouchesVaultAndStoredClearIsExplicit()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-vault-test-"+Guid.NewGuid().ToString("N"));try{var store=new ProfileStore(dir);var vault=new MemoryVault();var repo=new ProfileRepository(store,vault);var profile=new ConnectionProfile("name","group","synthetic",22,"user");
   var unsaved=await repo.SaveAsync(null,new(profile,"one-time","",false,false),CancellationToken.None);Assert.False(unsaved.HasPassword);Assert.Equal(0,vault.Reads+vault.Writes+vault.Deletes);
   var saved=await repo.SaveAsync(unsaved,new(unsaved,"saved","",true,false),CancellationToken.None);var cleared=await repo.SaveAsync(saved,new(saved,"one-time","",false,false),CancellationToken.None);Assert.False(cleared.HasPassword);Assert.Empty(vault.Values);
   var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"profiles.json")));Assert.False(data.RootElement[0].GetProperty("HasPassword").GetBoolean());Assert.DoesNotContain("one-time",data.RootElement.GetRawText());
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Fact]public async Task MetadataChangedWhileVaultWasWritingIsPreservedAndSecretRolledBack()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-vault-edit-"+Guid.NewGuid().ToString("N"));try{var store=new ProfileStore(dir);var vault=new MemoryVault();var repo=new ProfileRepository(store,vault);var p=await repo.SaveAsync(null,new(new("initial","group","host",22,"user"),"old","",true,false),CancellationToken.None);
   vault.AfterWrite=()=>store.Save(new[]{p with{Name="external edit"}});await Assert.ThrowsAsync<IOException>(()=>repo.SaveAsync(p,new(p with{Name="my edit"},"new","",true,false),CancellationToken.None));Assert.Equal("external edit",Assert.Single(store.Load()).Name);Assert.Equal("old",await repo.PasswordAsync(p,CancellationToken.None));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Fact]public async Task ChangingEndpointOrProtocolCannotImplicitlyTransferSavedPassword()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-credential-realm-"+Guid.NewGuid().ToString("N"));try{
   var store=new ProfileStore(dir);var vault=new MemoryVault();var repo=new ProfileRepository(store,vault);var profile=await repo.SaveAsync(null,new(new("name","group","original.invalid",22,"user"),"synthetic-secret","",true,false),CancellationToken.None);
   foreach(var changed in new[]{profile with{Host="other.invalid"},profile with{Port=21,Protocol="ftp"},profile with{User="other"}}){
    int before=vault.Reads+vault.Writes+vault.Deletes;
    await Assert.ThrowsAsync<IOException>(()=>repo.SaveAsync(profile,new(changed,"","",true,false),CancellationToken.None));Assert.Equal(before,vault.Reads+vault.Writes+vault.Deletes);Assert.Equal(profile,Assert.Single(store.Load()));
   }
   Assert.True(ProfileRepository.CanReusePassword(profile,profile with{Protocol="sftp"}));
   var renamed=await repo.SaveAsync(profile,new(profile with{Name="renamed"},"","",true,false),CancellationToken.None);Assert.Equal("synthetic-secret",await repo.PasswordAsync(renamed,CancellationToken.None));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Fact]public async Task SwitchingAwayFromPasswordClearsItInsteadOfDemandingPasswordForNewEndpoint()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-credential-mode-"+Guid.NewGuid().ToString("N"));try{
   var store=new ProfileStore(dir);var vault=new MemoryVault();var repo=new ProfileRepository(store,vault);var previous=await repo.SaveAsync(null,new(new("name","group","original.invalid",22,"user"),"synthetic-secret","",true,false),CancellationToken.None);
   var next=previous with{Host="other.invalid",AuthMode="key",KeyPath="/synthetic-key"};
   var result=await repo.SaveAsync(previous,new(next,"","",true,false),CancellationToken.None);Assert.False(result.HasPassword);Assert.Empty(vault.Values);
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Fact]public async Task KnownHostTestDoesNotWriteAndChangedFingerprintRequiresExplicitDecision()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-hosts-"+Guid.NewGuid().ToString("N"));try{var hosts=new KnownHosts(dir);int asks=0;
   Assert.True(await hosts.VerifyAsync("Host",22,"SHA256:first",false,(q,_)=>{asks++;Assert.Null(q.Previous);return Task.FromResult(true);},CancellationToken.None));Assert.False(Directory.Exists(dir));
   Assert.True(await hosts.VerifyAsync("Host",22,"SHA256:first",true,(q,_)=>Task.FromResult(true),CancellationToken.None));
   Assert.True(await hosts.VerifyAsync("host",22,"SHA256:first",true,(q,_)=>throw new Exception("must not prompt same key"),CancellationToken.None));
   Assert.False(await hosts.VerifyAsync("host",22,"SHA256:changed",true,(q,_)=>{Assert.True(q.Changed);Assert.Equal("SHA256:first",q.Previous);return Task.FromResult(false);},CancellationToken.None));Assert.Contains("SHA256:first",File.ReadAllText(Path.Combine(dir,"known-hosts.json")));Assert.DoesNotContain("SHA256:changed",File.ReadAllText(Path.Combine(dir,"known-hosts.json")));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Fact]public void MacVaultNativeSymbolsLoadWithoutAccessingAnyCredentials(){if(OperatingSystem.IsMacOS()){var report=JsonSerializer.Serialize(OsCredentialVault.ProbeNativeSymbols());Assert.Contains("\"CredentialsAccessed\":false",report);}}
}
