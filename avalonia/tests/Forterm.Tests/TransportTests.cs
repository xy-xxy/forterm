using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class TransportTests
{
 [Fact] public async Task ShellSftpShareOneAuthenticationAndClosingChannelPreservesOther()
 {
  using var server=new Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","synthetic-only",(fp,_)=>Task.FromResult(fp==server.Fingerprint),ct.Token);
  using var shell=await session.OpenShellAsync(80,24,ct.Token);
  using(var files=await session.OpenFilesAsync(ct.Token))
  {
   byte[] payload=new byte[1024*1024+13];Random.Shared.NextBytes(payload);
   await files.UploadNewAsync(new MemoryStream(payload),"/unicode 文件 % #.bin",ct.Token);
   using var output=new MemoryStream();await files.Client.DownloadFileAsync("/unicode 文件 % #.bin",output,ct.Token);Assert.Equal(payload,output.ToArray());
   await Assert.ThrowsAnyAsync<Exception>(()=>files.UploadNewAsync(new MemoryStream(new byte[]{1}),"/unicode 文件 % #.bin",ct.Token));
  }
  shell.Resize(111,37);await shell.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("\t\u0003csharp-input"),ct.Token);
  await shell.WriterStream.FlushAsync(ct.Token);
  var read=new byte[65536];string text="";while(!text.Contains("csharp-input")){int n=await shell.ReaderStream.ReadAsync(read,ct.Token);Assert.True(n>0);text+=Encoding.UTF8.GetString(read,0,n);}
  Assert.Contains('\t',text);Assert.Contains('\x03',text);
  using var second=await session.OpenShellAsync(90,28,ct.Token);shell.Dispose();session.Dispose();
  await second.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("second-survives"),ct.Token);
  await second.WriterStream.FlushAsync(ct.Token);
  text="";while(!text.Contains("second-survives")){int n=await second.ReaderStream.ReadAsync(read,ct.Token);Assert.True(n>0);text+=Encoding.UTF8.GetString(read,0,n);}
  var stats=server.Stats();Assert.Equal(1,stats.GetProperty("connections").GetInt32());Assert.Equal(1,stats.GetProperty("auth").GetInt32());Assert.Equal(2,stats.GetProperty("shells").GetInt32());Assert.Equal(1,stats.GetProperty("sftp").GetInt32());Assert.Contains(stats.GetProperty("resizes").EnumerateArray(),v=>v[0].GetInt32()==111&&v[1].GetInt32()==37);
 }
 [Fact] public async Task OwnedLocalUnixPtyRunsAndResizesWithoutAnyWindow()
 {
  if(OperatingSystem.IsWindows())return;
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
  using var pty=await Porta.Pty.PtyProvider.SpawnAsync(new Porta.Pty.PtyOptions{App="/bin/sh",CommandLine=new[]{"-c","printf 'owned-pty-checkpoint\\n'; stty size"},Cols=93,Rows=27,Cwd=Path.GetTempPath()},timeout.Token);
  pty.Resize(93,27);var buffer=new byte[4096];var output=new StringBuilder();
  while(!output.ToString().Contains("27 93")){int n=await pty.ReaderStream.ReadAsync(buffer,timeout.Token);if(n==0)break;output.Append(Encoding.UTF8.GetString(buffer,0,n));}
  Assert.Contains("owned-pty-checkpoint",output.ToString());Assert.Contains("27 93",output.ToString());
 }
 [Fact] public void MetadataStoreIsSeparateAndHasNoCredentialFields()
 {
  string directory=Path.Combine(Path.GetTempPath(),"forterm-meta-"+Guid.NewGuid().ToString("N"));
  try{var store=new Forterm.Desktop.ProfileStore(directory);var p=new Forterm.Desktop.ConnectionProfile("name","group","synthetic.invalid",22,"user");store.Save(new[]{p});Assert.Equal(p,Assert.Single(store.Load()));string json=File.ReadAllText(Path.Combine(directory,"profiles.json"));using(var metadata=JsonDocument.Parse(json)){Assert.DoesNotContain(metadata.RootElement[0].EnumerateObject(),p=>p.Name.Equals("Password",StringComparison.OrdinalIgnoreCase)||p.Name.Equals("Passphrase",StringComparison.OrdinalIgnoreCase));}Assert.Single(Directory.GetFiles(directory));}finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
 }
 [Fact]public async Task TerminalBackpressureClosesOnlyItsChannelWithoutBlockingSftp()
 {
  using var fixture=new Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
  using var files=await session.OpenFilesAsync(ct.Token);using var shell=await session.OpenShellAsync(80,24,ct.Token);fixture.Flood();
  while(!shell.WaitForExit(0))await Task.Delay(10,ct.Token);Assert.Contains("4MiB",shell.CloseReason);
  await files.UploadNewAsync(new MemoryStream(new byte[]{7,8,9}),"/after-shell-backpressure",ct.Token);Assert.True(session.IsConnected);Assert.Equal(1,fixture.Stats().GetProperty("connections").GetInt32());
 }
 [Theory][InlineData("FIXTURE_DENY_SFTP")][InlineData("FIXTURE_DENY_SHELL")]
 public async Task RejectedChannelDoesNotCloseTransportOrReauthenticate(string deny)
 {
  using var server=new Fixture(deny);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
  if(deny=="FIXTURE_DENY_SFTP"){
   using var shell=await session.OpenShellAsync(80,24,ct.Token);await Assert.ThrowsAnyAsync<Exception>(()=>session.OpenFilesAsync(ct.Token));Assert.True(session.IsConnected);
   await shell.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("alive"),ct.Token);await shell.WriterStream.FlushAsync(ct.Token);var b=new byte[128];Assert.True(await shell.ReaderStream.ReadAsync(b,ct.Token)>0);
  }else{await Assert.ThrowsAnyAsync<Exception>(()=>session.OpenShellAsync(80,24,ct.Token));using var files=await session.OpenFilesAsync(ct.Token);await files.UploadNewAsync(new MemoryStream(new byte[]{1,2,3}),"/still-alive",ct.Token);}
  Assert.Equal(1,server.Stats().GetProperty("auth").GetInt32());
 }
 internal sealed class Fixture:IDisposable
 {
  readonly Process process;public int Port{get;}public string Fingerprint{get;}public string PrivateKeyPath{get;}
  public Fixture(string? deny=null,string? authMode=null,bool encryptedKey=false,double? statDelay=null,bool posix=false,double? commitDelay=null,bool forward=false,bool silentGlobal=false,string keyAlgorithm="rsa",double? listDelay=null,string? realSftp=null){var root=FindRoot();var psi=new ProcessStartInfo(Path.Combine(root,"tooling/fixture-venv/bin/python3")){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};psi.ArgumentList.Add(Path.Combine(root,"tooling/ssh_fixture.py"));if(deny!=null)psi.Environment[deny]="1";if(listDelay!=null)psi.Environment["FIXTURE_LIST_DELAY"]=listDelay.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);if(authMode!=null)psi.Environment["FIXTURE_AUTH"]=authMode;if(encryptedKey)psi.Environment["FIXTURE_KEY_ENCRYPTED"]="1";psi.Environment["FIXTURE_USERKEY_ALGO"]=keyAlgorithm;if(silentGlobal)psi.Environment["FIXTURE_SILENT_GLOBAL"]="1";if(realSftp!=null)psi.Environment["FIXTURE_REAL_SFTP"]=realSftp;if(forward)psi.Environment["FIXTURE_FORWARD"]="1";if(posix)psi.Environment["FIXTURE_POSIX"]="1";if(commitDelay!=null)psi.Environment["FIXTURE_COMMIT_DELAY"]=commitDelay.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);if(statDelay!=null)psi.Environment["FIXTURE_STAT_DELAY"]=statDelay.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);process=Process.Start(psi)!;var line=process.StandardOutput.ReadLine();if(line==null)throw new Exception(process.StandardError.ReadToEnd());var data=JsonDocument.Parse(line).RootElement;Port=data.GetProperty("port").GetInt32();Fingerprint=data.GetProperty("fingerprint").GetString()!;PrivateKeyPath=data.GetProperty("privateKeyPath").GetString()!;}
  public void WrongSudoPrompt(){process.StandardInput.WriteLine("wrong-sudo-prompt");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("wrongPromptReady").GetBoolean());}
  public void SwapPermission(){process.StandardInput.WriteLine("swap-permission");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("swapReady").GetBoolean());}
  public void Large(){process.StandardInput.WriteLine("large");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("largeReady").GetBoolean());}
  public void SeedTree(){process.StandardInput.WriteLine("seed-tree");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("treeReady").GetBoolean());}
  public void SeedText(){process.StandardInput.WriteLine("seed-text");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("textReady").GetBoolean());}
  public void MakeLinks(){process.StandardInput.WriteLine("make-links");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("linksReady").GetBoolean());}
  public JsonElement Files(){process.StandardInput.WriteLine("files");process.StandardInput.Flush();return JsonDocument.Parse(process.StandardOutput.ReadLine()!).RootElement.Clone();}
  public void Emit(){process.StandardInput.WriteLine("emit");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("emitted").GetBoolean());}
  public void Flood(){process.StandardInput.WriteLine("flood");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("floodStarted").GetBoolean());}
  public void Rekey(){process.StandardInput.WriteLine("rekey");process.StandardInput.Flush();using var report=JsonDocument.Parse(process.StandardOutput.ReadLine()!);Assert.True(report.RootElement.GetProperty("rekeyed").GetBoolean());}
  public JsonElement Stats(){process.StandardInput.WriteLine("stats");process.StandardInput.Flush();return JsonDocument.Parse(process.StandardOutput.ReadLine()!).RootElement.Clone();}
  public void Dispose(){if(!process.HasExited){process.StandardInput.WriteLine("stop");process.StandardInput.Flush();if(!process.WaitForExit(2000))process.Kill(true);}process.Dispose();}
 }
 internal static string FindRoot(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p!=null&&!File.Exists(Path.Combine(p.FullName,"global.json")))p=p.Parent;return p?.FullName??throw new Exception("No test root");}
}
