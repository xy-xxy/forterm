using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
/// <summary>"新建文件夹" against OpenSSH's own sftp-server (2026-11-27, a real bastion): the request said "permissions follow"
/// and did not send them, which OpenSSH takes as a broken message, ending the file channel without an answer. The
/// loopback fixture now refuses such a request the same way, and where OpenSSH's sftp-server is installed it serves SFTP
/// itself, in a temporary directory.</summary>
public sealed class StrictSftpTests
{
 static string? SftpServer()=>new[]{"/usr/libexec/sftp-server","/usr/lib/openssh/sftp-server","/usr/libexec/openssh/sftp-server","/usr/lib/ssh/sftp-server"}.FirstOrDefault(File.Exists);
 [Fact]
 public async Task ADirectoryIsCreatedWithItsPermissionsSent()
 {
  using var fixture=new TransportTests.Fixture(posix:true);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));
  using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
  using var sftp=await session.OpenFilesAsync(ct.Token);var operations=new RemoteFileOperations();var parent=await operations.DirectoryAsync(sftp,"/",ct.Token);
  await operations.CreateDirectoryAsync(parent,"created",ct.Token);
  var stats=fixture.Stats();
  Assert.Equal(0,stats.GetProperty("incomplete").GetInt32());
  Assert.Equal(448,Assert.Single(stats.GetProperty("mkdirModes").EnumerateArray()).GetInt32());
  // The channel goes on: the next request is answered.
  Assert.Equal(448u,(await sftp.Client.LStatRawAsync("/created",ct.Token)).PermissionBits);
 }
 [Fact]
 public async Task OpenSshsOwnServerCreatesTheDirectory()
 {
  Assert.SkipWhen(SftpServer() is null,"OpenSSH's sftp-server is not installed here");
  string dir=Directory.CreateTempSubdirectory("ft-real-sftp-").FullName;
  try
  {
   dir=Path.GetFullPath(new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName??dir);
   if(OperatingSystem.IsMacOS()&&dir.StartsWith("/var/",StringComparison.Ordinal))dir="/private"+dir;
   using var fixture=new TransportTests.Fixture(realSftp:SftpServer());using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(25));
   using var session=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
   using var sftp=await session.OpenFilesAsync(ct.Token);var operations=new RemoteFileOperations();var parent=await operations.DirectoryAsync(sftp,dir,ct.Token);
   await operations.CreateDirectoryAsync(parent,"created",ct.Token);
   Assert.True(Directory.Exists(Path.Combine(dir,"created")));
   if(!OperatingSystem.IsWindows())Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute,File.GetUnixFileMode(Path.Combine(dir,"created")));
   Assert.Equal(448u,(await sftp.Client.LStatRawAsync(dir+"/created",ct.Token)).PermissionBits);
   Assert.False(fixture.Stats().TryGetProperty("realSftpExit",out _)); // still serving
  }
  finally{Directory.Delete(dir,true);}
 }
}
