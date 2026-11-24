using Forterm.Core;
using System.Text;
using Xunit;
namespace Forterm.Tests;
public sealed class SafeTransferTests
{
 sealed class Progress(Action<TransferProgress> changed):IProgress<TransferProgress>{public void Report(TransferProgress p)=>changed(p);}
 sealed class Scope:IDisposable
 {
  public readonly string Local=Path.Combine(Path.GetTempPath(),"forterm-safe-"+Guid.NewGuid().ToString("N"));public readonly TransportTests.Fixture Server;public SharedSshSession Session=null!;public SftpLease Files=null!;public readonly CancellationTokenSource Timeout=new(TimeSpan.FromSeconds(25));
  Scope(bool posix,double? delay){Server=new TransportTests.Fixture(posix:posix,commitDelay:delay);}
  public static async Task<Scope> Open(bool posix=false,double? delay=null){var s=new Scope(posix,delay);Directory.CreateDirectory(s.Local);s.Session=await SharedSshSession.ConnectAsync("127.0.0.1",s.Server.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),s.Timeout.Token);s.Files=await s.Session.OpenFilesAsync(s.Timeout.Token);return s;}
  public void Dispose(){Files?.Dispose();Session?.Dispose();Server.Dispose();Timeout.Dispose();if(Directory.Exists(Local))Directory.Delete(Local,true);}
 }
 [Fact]public async Task UploadAndDownloadCommitVerifiedBytesAndDoNotLeaveTemps()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;string input=Path.Combine(s.Local,"source % 中文.bin");byte[] content=new byte[1024*1024+3];Random.Shared.NextBytes(content);await File.WriteAllBytesAsync(input,content,ct);
  var directory=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var local=await engine.CaptureLocalAsync(input,ct);Assert.Equal(content.Length,local.Stamp!.Length);var target=await engine.CaptureRemoteAsync(directory,"dest.bin",ct);
  await engine.UploadAsync(new(directory,target,local,false),new Progress(_=>{}),ct);
  string destination=Path.Combine(s.Local,"download.bin");var remote=await engine.CaptureRemoteAsync(directory,"dest.bin",ct);var output=await engine.CaptureLocalAsync(destination,ct);
  await engine.DownloadAsync(new(directory,remote,output,false),new Progress(_=>{}),ct);Assert.Equal(content,await File.ReadAllBytesAsync(destination,ct));Assert.Contains(s.Server.Stats().GetProperty("openModes").EnumerateArray(),x=>x.ValueKind==System.Text.Json.JsonValueKind.Number&&x.GetInt32()==384);Assert.DoesNotContain(s.Files.Client.ListDirectory("/"),f=>f.Name.StartsWith(".forterm-"));Assert.DoesNotContain(Directory.GetFiles(s.Local),p=>Path.GetFileName(p).StartsWith(".forterm-"));
 }
 [Fact]public async Task CancelDownloadPreservesOldTargetAndRemovesOwnedTemp()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(new byte[1024*1024]),"/remote.bin",ct);string output=Path.Combine(s.Local,"old.bin");await File.WriteAllTextAsync(output,"old-stays",ct);
  var directory=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var remote=await engine.CaptureRemoteAsync(directory,"remote.bin",ct);var local=await engine.CaptureLocalAsync(output,ct);using var cancel=new CancellationTokenSource();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>engine.DownloadAsync(new(directory,remote,local,true),new Progress(p=>{if(p.Phase=="传输中"&&p.Bytes>0)cancel.Cancel();}),cancel.Token));Assert.Equal("old-stays",await File.ReadAllTextAsync(output,ct));Assert.Single(Directory.GetFiles(s.Local));
 }
 [Fact]public async Task ChangesAfterPickerOrWhileQueuedAbortBeforeOverwriting()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("source-content")),"/remote.bin",ct);string output=Path.Combine(s.Local,"old.bin");await File.WriteAllTextAsync(output,"old",ct);
  var directory=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var remote=await engine.CaptureRemoteAsync(directory,"remote.bin",ct);var local=await engine.CaptureLocalAsync(output,ct);await File.WriteAllTextAsync(output,"new-edit-during-picker",ct);
  await Assert.ThrowsAsync<IOException>(()=>engine.DownloadAsync(new(directory,remote,local,true),new Progress(_=>{}),ct));Assert.Equal("new-edit-during-picker",await File.ReadAllTextAsync(output,ct));
  local=await engine.CaptureLocalAsync(output,ct);bool edited=false;
  await Assert.ThrowsAsync<IOException>(()=>engine.DownloadAsync(new(directory,remote,local,true),new Progress(p=>{if(p.Phase=="校验内容"&&!edited){edited=true;File.WriteAllText(output,"new-edit-during-transfer");}}),ct));Assert.Equal("new-edit-during-transfer",await File.ReadAllTextAsync(output,ct));
 }
 [Fact]public async Task UnsupportedRemoteOverwriteNeverDeletesOldFile()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("old")),"/dest.bin",ct);string input=Path.Combine(s.Local,"new.bin");await File.WriteAllTextAsync(input,"new",ct);var d=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var target=await engine.CaptureRemoteAsync(d,"dest.bin",ct);var local=await engine.CaptureLocalAsync(input,ct);
  Assert.False(s.Files.Client.SupportsAtomicReplacement);await Assert.ThrowsAsync<IOException>(()=>engine.UploadAsync(new(d,target,local,true),new Progress(_=>{}),ct));using var output=new MemoryStream();await s.Files.Client.DownloadFileAsync("/dest.bin",output,ct);Assert.Equal("old",Encoding.UTF8.GetString(output.ToArray()));
 }
 [Fact]public async Task ClosedOriginalSourceIsNotRetargetedToAnotherConnection()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(new byte[]{1,2,3}),"/source.bin",ct);string output=Path.Combine(s.Local,"old.bin");await File.WriteAllTextAsync(output,"old",ct);var d=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var remote=await engine.CaptureRemoteAsync(d,"source.bin",ct);var local=await engine.CaptureLocalAsync(output,ct);
  s.Files.Dispose();using var replacement=await s.Session.OpenFilesAsync(ct);Assert.NotEqual(d.SourceId,replacement.SourceId);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>engine.DownloadAsync(new(d,remote,local,true),new Progress(_=>{}),ct));Assert.Equal("old",await File.ReadAllTextAsync(output,ct));Assert.True(s.Session.IsConnected);
 }
 [Fact]public async Task RemoteLinksAreCheckedBeforeCanonicalResolutionAndMetadataTimeoutIsNotMissing()
 {
  using(var s=await Scope.Open()){
   s.Server.MakeLinks();var engine=new SafeTransfers();await Assert.ThrowsAsync<IOException>(()=>engine.CaptureDirectoryAsync(s.Files,"/link-dir",s.Timeout.Token));var d=await engine.CaptureDirectoryAsync(s.Files,"/",s.Timeout.Token);await Assert.ThrowsAsync<IOException>(()=>engine.CaptureRemoteAsync(d,"link-file",s.Timeout.Token));
  }
  using var server=new TransportTests.Fixture(statDelay:0.5);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(10));using var session=await SharedSshSession.ConnectAsync("127.0.0.1",server.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);using var files=await session.OpenFilesAsync(ct.Token);
  var bounded=new SafeTransfers(new(TimeSpan.FromMilliseconds(40),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(10)));await Assert.ThrowsAsync<TimeoutException>(()=>bounded.CaptureDirectoryAsync(files,"/",ct.Token));Assert.Empty(server.Stats().GetProperty("openModes").EnumerateArray());Assert.True(session.IsConnected);
 }
 [Fact]public async Task ClosingSourceDuringDownloadKeepsOldFileAndRemovedParentIsNotRebound()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(new byte[65536]),"/source.bin",ct);var d=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var remote=await engine.CaptureRemoteAsync(d,"source.bin",ct);
  string child=Path.Combine(s.Local,"child");Directory.CreateDirectory(child);string targetPath=Path.Combine(child,"target");await File.WriteAllTextAsync(targetPath,"old",ct);var target=await engine.CaptureLocalAsync(targetPath,ct);
  Directory.Move(child,child+"-previous");Directory.CreateDirectory(child);await Assert.ThrowsAsync<IOException>(()=>engine.DownloadAsync(new(d,remote,target,true),new Progress(_=>{}),ct));Assert.False(File.Exists(targetPath));Assert.Equal("old",await File.ReadAllTextAsync(Path.Combine(child+"-previous","target"),ct));
  await File.WriteAllTextAsync(targetPath,"current-old",ct);target=await engine.CaptureLocalAsync(targetPath,ct);
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>engine.DownloadAsync(new(d,remote,target,true),new Progress(p=>{if(p.Phase=="校验内容")s.Files.Dispose();}),ct));Assert.Equal("current-old",await File.ReadAllTextAsync(targetPath,ct));Assert.DoesNotContain(Directory.GetFiles(child),p=>Path.GetFileName(p).StartsWith(".forterm-"));
 }
 [Fact]public async Task ConfirmedAtomicRemoteReplacementPreservesModeAndCancellationKeepsOldBytes()
 {
  using var s=await Scope.Open(posix:true);var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("old-content")),"/dest.bin",ct);s.Files.Client.ChangePermissions("/dest.bin",640);string input=Path.Combine(s.Local,"new");await File.WriteAllTextAsync(input,"new-content",ct);
  var d=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var target=await engine.CaptureRemoteAsync(d,"dest.bin",ct);var local=await engine.CaptureLocalAsync(input,ct);Assert.True(s.Files.Client.SupportsAtomicReplacement);using var cancel=new CancellationTokenSource();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>engine.UploadAsync(new(d,target,local,true),new Progress(p=>{if(p.Phase=="校验内容")cancel.Cancel();}),cancel.Token));
  using(var old=new MemoryStream()){await s.Files.Client.DownloadFileAsync("/dest.bin",old,ct);Assert.Equal("old-content",System.Text.Encoding.UTF8.GetString(old.ToArray()));}
  await engine.UploadAsync(new(d,target,local,true),new Progress(_=>{}),ct);var result=await engine.CaptureRemoteAsync(d,"dest.bin",ct);Assert.Equal(416u,result.Stamp!.Mode);using var bytes=new MemoryStream();await s.Files.Client.DownloadFileAsync("/dest.bin",bytes,ct);Assert.Equal("new-content",System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
 }
 [Fact]public async Task LostCommitReceiptIsUncertainAndStopsOnlyOriginalFileChannel()
 {
  using var s=await Scope.Open(posix:true,delay:2.0);var engine=new SafeTransfers(new(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(15)));var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(new byte[]{1,2,3}),"/old.bin",ct);string input=Path.Combine(s.Local,"new");await File.WriteAllBytesAsync(input,new byte[]{7,8,9},ct);var d=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var target=await engine.CaptureRemoteAsync(d,"old.bin",ct);var local=await engine.CaptureLocalAsync(input,ct);
  await Assert.ThrowsAsync<CommitUncertainException>(()=>engine.UploadAsync(new(d,target,local,true),new Progress(_=>{}),ct));Assert.False(s.Files.IsAvailable);Assert.True(s.Session.IsConnected);using var shell=await s.Session.OpenShellAsync(80,24,ct);await shell.WriterStream.WriteAsync(new byte[]{65},ct);await shell.WriterStream.FlushAsync(ct);var data=new byte[128];Assert.True(await shell.ReaderStream.ReadAsync(data,ct)>0);
 }
 [Fact]public async Task SuccessfulLocalReplacementRetainsPriorMode()
 {
  if(OperatingSystem.IsWindows())return;using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;await s.Files.UploadNewAsync(new MemoryStream(new byte[]{2,3,4}),"/source.bin",ct);var d=await engine.CaptureDirectoryAsync(s.Files,"/",ct);var remote=await engine.CaptureRemoteAsync(d,"source.bin",ct);string output=Path.Combine(s.Local,"old");await File.WriteAllBytesAsync(output,new byte[]{9},ct);var mode=UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead;File.SetUnixFileMode(output,mode);var target=await engine.CaptureLocalAsync(output,ct);await engine.DownloadAsync(new(d,remote,target,true),new Progress(_=>{}),ct);Assert.Equal(mode,File.GetUnixFileMode(output));Assert.Equal(new byte[]{2,3,4},await File.ReadAllBytesAsync(output,ct));
 }
 [Fact]public void LocalSymlinksAndChangedDirectoryIdentityAreRejected()
 {
  string root=Path.Combine(Path.GetTempPath(),"forterm-localguard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{string file=Path.Combine(root,"regular");File.WriteAllText(file,"abc");var stamp=LocalStamp.Read(file);Assert.NotNull(stamp);Assert.Equal(3,stamp.Length);Assert.False(stamp.Directory);File.CreateSymbolicLink(Path.Combine(root,"link"),file);Assert.Throws<IOException>(()=>LocalStamp.Read(Path.Combine(root,"link")));}finally{Directory.Delete(root,true);}
 }
 [Fact]public async Task MixedDirectoryAndTargetPlansCannotWriteToAnotherDirectory()
 {
  using var s=await Scope.Open();var engine=new SafeTransfers();var ct=s.Timeout.Token;string path=Path.Combine(s.Local,"source");await File.WriteAllTextAsync(path,"data",ct);var a=await engine.CaptureDirectoryAsync(s.Files,"/a",ct);var b=await engine.CaptureDirectoryAsync(s.Files,"/b",ct);var target=await engine.CaptureRemoteAsync(b,"must-not-write",ct);var input=await engine.CaptureLocalAsync(path,ct);
  await Assert.ThrowsAsync<IOException>(()=>engine.UploadAsync(new(a,target,input,false),new Progress(_=>{}),ct));Assert.False(s.Server.Files().TryGetProperty("b/must-not-write",out _));
 }
 [Fact]public async Task ClosingBoundSourceCancelsWaitingTaskWithoutRunningIt()
 {
  using var queue=new TransferQueue();var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);queue.Add("busy","other",async(p,ct)=>await release.Task.WaitAsync(ct));using var source=new CancellationTokenSource();int calls=0;var waiting=queue.Add("waiting","original",(p,ct)=>{calls++;return Task.CompletedTask;},source.Token);source.Cancel();Assert.Equal(TransferState.Cancelled,queue.Snapshot().Single(s=>s.Id==waiting.Id).State);release.SetResult();await Task.Delay(30);Assert.Equal(0,calls);
 }
 [Fact]public async Task QueueIsSerialAndCancelledWaitingItemDoesNotRun()
 {
  using var queue=new TransferQueue();int active=0,maximum=0,called=0;var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var first=queue.Add("first","original-source",async(p,ct)=>{int n=Interlocked.Increment(ref active);maximum=Math.Max(maximum,n);Interlocked.Increment(ref called);await release.Task.WaitAsync(ct);Interlocked.Decrement(ref active);});
  var second=queue.Add("second","other-source",(p,ct)=>{Interlocked.Increment(ref called);return Task.CompletedTask;});second.Cancel();release.SetResult();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  while(queue.Snapshot().Any(s=>s.State is TransferState.Queued or TransferState.Running))await Task.Delay(5,timeout.Token);Assert.Equal(1,maximum);Assert.Equal(1,called);Assert.Equal(TransferState.Completed,queue.Snapshot().Single(s=>s.Id==first.Id).State);Assert.Equal(TransferState.Cancelled,queue.Snapshot().Single(s=>s.Id==second.Id).State);
 }
}
