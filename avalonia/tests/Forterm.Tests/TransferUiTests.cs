using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class TransferUiTests
{
 sealed class Pickers:IFilePickers
 {
  public readonly TaskCompletionSource<PickedLocalFile?> Pick=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Called;
  public Task<PickedLocalFile?> UploadAsync(Window owner){Called=true;return Pick.Task;}
  public Task<string?> DownloadAsync(Window owner,string name)=>Task.FromResult<string?>(null);
 }
 static async Task Until(Func<bool> condition){using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(12));while(!condition()){ct.Token.ThrowIfCancellationRequested();await Task.Delay(5,ct.Token);}}
 static Button Button(SessionPane pane,string text)=>pane.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,text));
 static async Task Connect(SessionPane pane,Window owner)
 {
  var connected=pane.ConnectAsync("synthetic-only");await Until(()=>owner.OwnedWindows.Count>0);var prompt=owner.OwnedWindows.Single();prompt.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"确认")).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await connected;owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
 }
 [AvaloniaFact]public async Task TransferWorkRunsOffDispatcher()
 {
  using var queue=new TransferQueue();bool? dispatcher=null;
  var ticket=queue.Add("background","synthetic",(_,_)=>{dispatcher=Dispatcher.UIThread.CheckAccess();return Task.CompletedTask;});
  var result=await ticket.Completion;Assert.Equal(TransferState.Completed,result.State);Assert.False(dispatcher);
 }
 [AvaloniaFact]public async Task UploadPickerKeepsOriginalDirectoryDespitePathBarChanges()
 {
  using var fixture=new TransportTests.Fixture();using var queue=new TransferQueue();var picker=new Pickers();var owner=new Window{Width=1050,Height=650};var pane=new SessionPane(owner,new("synthetic","tests","127.0.0.1",fixture.Port,"synthetic"),true,queue,picker);owner.Content=pane;owner.Show();await Connect(pane,owner);
  var path=pane.GetVisualDescendants().OfType<TextBox>().Single(e=>AutomationProperties.GetName(e)=="远程路径");path.Text="/a";
  string input=Path.Combine(Path.GetTempPath(),"forterm-picker-"+Guid.NewGuid().ToString("N"));await File.WriteAllTextAsync(input,"synthetic upload content");
  try{
   Button(pane,"上传").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Until(()=>picker.Called);path.Text="/b";picker.Pick.SetResult(new(input,"picked.bin"));await Until(()=>queue.Snapshot().Count==1&&queue.Snapshot()[0].State is TransferState.Completed or TransferState.Failed);
   Assert.Equal(TransferState.Completed,queue.Snapshot()[0].State);var files=fixture.Files();Assert.True(files.TryGetProperty("a/picked.bin",out _));Assert.False(files.TryGetProperty("b/picked.bin",out _));Assert.Contains("/a",queue.Snapshot()[0].Source);
  }finally{pane.Dispose();owner.Close();File.Delete(input);}
 }
 [AvaloniaFact]public async Task ClosingViewBeforePickerReturnsDoesNotEnqueueOrWrite()
 {
  using var fixture=new TransportTests.Fixture();using var queue=new TransferQueue();var picker=new Pickers();var owner=new Window{Width=1050,Height=650};var pane=new SessionPane(owner,new("synthetic","tests","127.0.0.1",fixture.Port,"synthetic"),true,queue,picker);owner.Content=pane;owner.Show();await Connect(pane,owner);
  Button(pane,"上传").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Until(()=>picker.Called);pane.Dispose();picker.Pick.SetResult(new("/must-not-open","must-not-write.bin"));await Task.Delay(60);Assert.Empty(queue.Snapshot());Assert.DoesNotContain(fixture.Files().EnumerateObject(),p=>p.Name.EndsWith("must-not-write.bin"));owner.Close();
 }
 [AvaloniaFact]public async Task QueueSmallLayoutRetainsFileActionsAndCancelledRows()
 {
  using var fixture=new TransportTests.Fixture();using var queue=new TransferQueue();var owner=new Window{Width=700,Height=433};var pane=new SessionPane(owner,new("synthetic","tests","127.0.0.1",fixture.Port,"synthetic"),true,queue);owner.Content=pane;owner.Show();await Connect(pane,owner);
  for(int i=0;i<25;i++){var ticket=queue.Add("合成队列布局 "+i,"合成来源 /sample",async(p,ct)=>await Task.Delay(Timeout.Infinite,ct));if(i>1)queue.Cancel(ticket.Id);}
  await Task.Delay(30);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();var panel=pane.GetVisualDescendants().OfType<TransferPanel>().Single();Assert.True(panel.ViewportHeight<=76);
  foreach(var text in new[]{"上传","下载","刷新"}){var button=Button(pane,text);var point=button.TranslatePoint(default,owner)!.Value;Assert.True(point.Y+button.Bounds.Height<=owner.Height);}
  var folder=UiEvidence.DirectoryPath;Directory.CreateDirectory(folder);owner.CaptureRenderedFrame()?.Save(Path.Combine(folder,"transfer-queue-small.png"));pane.Dispose();owner.Close();
 }
}
