using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Xunit;
namespace Forterm.Tests;
public sealed class WorkspaceUiTests
{
 static async Task Until(Func<bool> check){using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(12));while(!check()){ct.Token.ThrowIfCancellationRequested();await Task.Delay(5,ct.Token);}}
 static void Confirm(Window owner)=>owner.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"确认")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 [AvaloniaFact]public async Task ClonedWorkspaceHasIndependentVtAndSurvivesOriginalViewCloseWithOneAuthentication()
 {
  using var server=new TransportTests.Fixture();var owner=new Window{Width=1050,Height=650};using var queue=new TransferQueue();var p=new ConnectionProfile("synthetic","g","127.0.0.1",server.Port,"synthetic");var original=new SessionPane(owner,p,false,queue);owner.Content=original;owner.Show();var connecting=original.ConnectAsync("synthetic-only");await Until(()=>owner.OwnedWindows.Count>0);Confirm(owner);await connecting;
  var clone=new SessionPane(owner,p,false,queue);owner.Content=clone;owner.UpdateLayout();await clone.CloneFromAsync(original);original.Dispose();Assert.True(clone.HasUsableConnection);Assert.NotSame(original.Terminal.Terminal,clone.Terminal.Terminal);var view=clone.Terminal.GetVisualDescendants().OfType<TerminalView>().Single();view.Focus();owner.KeyTextInput("clone-survives");await Until(()=>server.Stats().GetProperty("input").EnumerateArray().Any(x=>x.GetString()!.Contains(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("clone-survives")).ToLowerInvariant())));Assert.Equal(1,server.Stats().GetProperty("auth").GetInt32());Assert.Equal(2,server.Stats().GetProperty("shells").GetInt32());Save(owner,"cloned-workspace");clone.Dispose();owner.Close();
 }
 [AvaloniaFact]public async Task SuccessfulConnectCollapsesNavigationButManualOverrideWins()
 {
  using var server=new TransportTests.Fixture();string dir=Path.Combine(Path.GetTempPath(),"forterm-nav-"+Guid.NewGuid().ToString("N"));new PreferenceStore(dir).Save(new(){AutoCollapseNavigation=true,KeepAliveEnabled=false});
  try{
   var owner=new MainWindow(dir,new CredentialStoreTests.MemoryVault());owner.Show();var plan=LaunchPlan.Parse(new[]{"-url",$"ssh://synthetic:synthetic-only@127.0.0.1:{server.Port}"});var open=owner.AcceptLaunch(plan);await Until(()=>owner.OwnedWindows.Count>0);var button=owner.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"导航"));button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Confirm(owner);await open;owner.UpdateLayout();Assert.True(owner.GetVisualDescendants().OfType<TreeView>().Single(t=>Avalonia.Automation.AutomationProperties.GetName(t)=="连接分组").IsEffectivelyVisible);Save(owner,"navigation-manual-override");owner.Close();
   owner=new MainWindow(dir,new CredentialStoreTests.MemoryVault());owner.Show();open=owner.AcceptLaunch(LaunchPlan.Parse(new[]{"-url",$"ssh://synthetic:synthetic-only@127.0.0.1:{server.Port}"}));await open;owner.UpdateLayout();Assert.False(owner.GetVisualDescendants().OfType<TreeView>().Single(t=>Avalonia.Automation.AutomationProperties.GetName(t)=="连接分组").IsEffectivelyVisible);Save(owner,"navigation-auto-collapsed");owner.Close();
  }finally{Directory.Delete(dir,true);}
 }
 [AvaloniaFact]public async Task TerminalKeepsReadingWhileLibraryIsShownAndResumesInput()
 {
  using var fixture=new TransportTests.Fixture();string dir=Path.Combine(Path.GetTempPath(),"forterm-terminal-lifetime-"+Guid.NewGuid().ToString("N"));var owner=new MainWindow(dir,new CredentialStoreTests.MemoryVault());owner.Show();
  try{
   var open=owner.AcceptLaunch(LaunchPlan.Parse(new[]{"-url",$"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"}));await Until(()=>owner.OwnedWindows.Count>0);Confirm(owner);await open;owner.UpdateLayout();var pane=Assert.Single(owner.Workspaces);Assert.True(pane.Terminal.IsLive);
   owner.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"连接库")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));fixture.Emit();
   await Until(()=>Enumerable.Range(0,pane.Terminal.Terminal.Buffer.Length).Any(i=>pane.Terminal.Terminal.Buffer.GetLine(i)?.TranslateToString(true).Contains("BACKGROUND OUTPUT")==true));Assert.True(pane.Terminal.IsLive);
   owner.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"工作区")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
   var view=pane.Terminal.GetVisualDescendants().OfType<TerminalView>().Single();view.Focus();owner.KeyTextInput("after-library");await Until(()=>fixture.Stats().GetProperty("input").GetArrayLength()>0);
   Assert.Equal(1,fixture.Stats().GetProperty("auth").GetInt32());Assert.Equal(1,fixture.Stats().GetProperty("shells").GetInt32());
  }finally{owner.Close();if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [AvaloniaFact]public void SmallToolbarAndPersistedThemeFontRemainReachable()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-appearance-"+Guid.NewGuid().ToString("N"));new PreferenceStore(dir).Save(new(){Appearance="light",UiFontSize=16,TerminalFontSize=18,KeepAliveSeconds=31});try{var window=new MainWindow(dir);window.Width=700;window.Height=433;window.Show();window.UpdateLayout();Assert.Equal(16,window.FontSize);foreach(var button in window.GetVisualDescendants().OfType<Button>().Where(b=>b.Content is string text&&text is "连接库" or "新建" or "本地终端" or "验证器" or "导航" or "工作区" or "⚙")){var point=button.TranslatePoint(default,window)!.Value;Assert.True(point.X>=0&&point.X+button.Bounds.Width<=window.Width,$"toolbar {button.Content}: {point} {button.Bounds}");}Save(window,"preferences-light-small");window.Close();}finally{Directory.Delete(dir,true);}
 }
 static void Save(Window window,string name){var folder=UiEvidence.DirectoryPath;Directory.CreateDirectory(folder);window.CaptureRenderedFrame()?.Save(Path.Combine(folder,name+".png"));}
}
