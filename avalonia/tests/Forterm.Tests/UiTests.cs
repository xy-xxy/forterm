using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Xunit;
[assembly:AvaloniaTestApplication(typeof(Forterm.Tests.TestApp))]
[assembly:AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
namespace Forterm.Tests;
public static class TestApp { public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}); }
public sealed class UiTests
{
 [AvaloniaFact] public void RealTerminalParsesAnsiAlternateCjkGraphemes()
 {
  var terminal=SessionPane.CreateTerminal();var window=new Window{Width=900,Height=500,Content=terminal};window.Show();window.UpdateLayout();Dispatcher.UIThread.RunJobs();
  terminal.Terminal.Write("\x1b[31mANSI\x1b[0m 中文 e\u0301 👩‍💻");
  Assert.Equal("A",terminal.Terminal.Buffer.Lines[0]![0].Content);
  terminal.Terminal.Write("\x1b[?1049hALT");Dispatcher.UIThread.RunJobs();Assert.True(terminal.IsAlternateBuffer);terminal.Terminal.Write("\x1b[?1049l");Dispatcher.UIThread.RunJobs();Assert.False(terminal.IsAlternateBuffer);
  var line=terminal.Terminal.Buffer.Lines[0]!;
  Assert.Equal("中",line[5].Content);Assert.Equal(2,line[5].Width);Assert.Equal("e\u0301",line[10].Content);Assert.Equal("👩‍💻",line[12].Content);Assert.Equal(2,line[12].Width);
  Save(window,"terminal-parser");terminal.Dispose();window.Close();
 }
 [AvaloniaFact] public async Task TerminalDispatchesRealKeysOverSharedSshAndResize()
 {
  using var fixture=new TransportTests.Fixture();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  using var session=await Forterm.Core.SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
  using var shell=await session.OpenShellAsync(80,24,ct.Token);
  var terminal=SessionPane.CreateTerminal();var entry=new TextBox();var grid=new Grid{RowDefinitions=new RowDefinitions("*,Auto")};grid.Children.Add(terminal);Grid.SetRow(entry,1);grid.Children.Add(entry);
  var window=new Window{Width=900,Height=500,Content=grid};window.Show();window.UpdateLayout();Dispatcher.UIThread.RunJobs();terminal.AttachConnection(shell);
  var view=terminal.GetVisualDescendants().OfType<TerminalView>().First();view.Focus();
  window.KeyPress(Key.Tab,RawInputModifiers.None,PhysicalKey.Tab,null);window.KeyRelease(Key.Tab,RawInputModifiers.None,PhysicalKey.Tab,null);
  window.KeyPress(Key.C,RawInputModifiers.Control,PhysicalKey.C,null);window.KeyRelease(Key.C,RawInputModifiers.Control,PhysicalKey.C,null);
  window.KeyTextInput("csharp-real-keys");
  await Task.Delay(200,ct.Token);Dispatcher.UIThread.RunJobs();
  var stats=fixture.Stats();string bytes=string.Concat(stats.GetProperty("input").EnumerateArray().Select(x=>x.GetString()));Assert.Contains("09",bytes);Assert.Contains("03",bytes);Assert.Contains(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("csharp-real-keys")).ToLowerInvariant(),bytes);
  Assert.True(view.IsFocused);
  await window.Clipboard!.SetTextAsync("paste-from-clipboard");await terminal.PasteAsync();await Task.Delay(100,ct.Token);
  string pasted=string.Concat(fixture.Stats().GetProperty("input").EnumerateArray().Select(x=>x.GetString()));Assert.Contains(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("paste-from-clipboard")).ToLowerInvariant(),pasted);
  var origin=view.TranslatePoint(default,window)!.Value;
  window.MouseDown(origin+new Vector(2,7),MouseButton.Left,RawInputModifiers.None);window.MouseMove(origin+new Vector(70,7),RawInputModifiers.LeftMouseButton);window.MouseUp(origin+new Vector(70,7),MouseButton.Left,RawInputModifiers.None);
  Assert.True(terminal.Terminal.Selection.HasSelection);Assert.True(await terminal.CopyAsync());Assert.False(string.IsNullOrEmpty(await window.Clipboard!.TryGetTextAsync()));
  entry.Focus();window.KeyTextInput("entry-only");Assert.Equal("entry-only",entry.Text);
  window.Width=1100;window.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(100,ct.Token);Assert.NotEmpty(fixture.Stats().GetProperty("resizes").EnumerateArray());
  using var files=await session.OpenFilesAsync(ct.Token);await files.UploadNewAsync(new MemoryStream(new byte[]{3,4,5}),"/parallel-ui",ct.Token);
  Save(window,"terminal-live-ssh");terminal.Dispose();window.Close();
 }
 [AvaloniaFact] public void ConnectionCardFooterAndLastItemReachableAfterResize()
 {
  var window=new ConnectionWindow();window.Show();window.UpdateLayout();Dispatcher.UIThread.RunJobs();Save(window,"connection-dark");
  foreach(var size in new[]{new Size(700,433),new Size(480,500),new Size(900,700)})
  {
   window.Width=size.Width;window.Height=size.Height;window.UpdateLayout();Dispatcher.UIThread.RunJobs();
   var scroll=window.GetVisualDescendants().OfType<ScrollViewer>().First();scroll.Offset=new Vector(0,double.MaxValue);window.UpdateLayout();
   var last=window.GetVisualDescendants().OfType<TextBlock>().First(x=>x.Text == "心跳与字号在偏好设置中配置。");var p=last.TranslatePoint(default,window)!.Value;
   Assert.InRange(p.Y,0,window.Height);Assert.True(p.Y+last.Bounds.Height<window.Height);
   foreach(var b in window.GetVisualDescendants().OfType<Button>().Where(x=>Equals(x.Content,"保存并连接"))) {var point=b.TranslatePoint(default,window)!.Value;Assert.True(point.Y+b.Bounds.Height<=window.Height);}
   Save(window,$"connection-{size.Width}");
  }
  Application.Current!.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;Dispatcher.UIThread.RunJobs();Save(window,"connection-light");Application.Current.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
  window.Close();
 }
 [AvaloniaFact] public async Task SixteenQuestionDialogCanReachLastFieldAndCancelsWithoutRetainingAnswers()
 {
  var owner=new Window{Width=700,Height=433};owner.Show();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(10));
  var questions=Enumerable.Range(0,16).Select(i=>new Forterm.Core.CredentialQuestion("Synthetic question "+i,false)).ToArray();
  var response=UI.AskCredentials(owner,new Forterm.Core.CredentialChallenge(2,true,true,"Synthetic only",questions),ct.Token);
  var dialog=owner.OwnedWindows.Single();dialog.Height=380;dialog.UpdateLayout();var fields=dialog.GetVisualDescendants().OfType<TextBox>().ToArray();Assert.Equal(16,fields.Length);fields[^1].Focus();fields[^1].BringIntoView();dialog.UpdateLayout();
  fields[^1].Text="synthetic-answer";var p=fields[^1].TranslatePoint(default,dialog)!.Value;Assert.True(p.Y>=0&&p.Y+fields[^1].Bounds.Height<dialog.Height);Save(dialog,"credential-last-field");
  ct.Cancel();Dispatcher.UIThread.RunJobs();Assert.Null(await response);Assert.All(fields,e=>Assert.Equal("",e.Text));owner.Close();
 }
 [AvaloniaFact] public async Task MissingUrlPasswordPromptsBeforeConnectingAndCancelDoesNotConsumeTicket()
 {
  using var fixture=new TransportTests.Fixture();var owner=new MainWindow(null);owner.Show();owner.UpdateLayout();
  var plan=Forterm.Core.LaunchPlan.Parse(new[]{"-url",$"ssh://synthetic@127.0.0.1:{fixture.Port}"},"xshell");
  var opening=owner.AcceptLaunch(plan);var dialog=owner.OwnedWindows.Single();Assert.Equal(0,fixture.Stats().GetProperty("connections").GetInt32());
  var cancel=dialog.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"取消"));cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await opening;Assert.Equal(0,fixture.Stats().GetProperty("connections").GetInt32());Assert.Equal("",plan.Connection!.Password);owner.Close();
 }
 [AvaloniaFact] public void ConnectionLibraryIsRealAvaloniaVisualTree(){var w=new MainWindow(null);w.Show();w.UpdateLayout();Dispatcher.UIThread.RunJobs();Assert.NotEmpty(w.GetVisualDescendants().OfType<TreeView>());Save(w,"library");w.Close();}
 static void Save(Window window,string name){var root=TransportTests.FindRoot();var path=Path.Combine(root,"evidence/ui-"+(typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0]));Directory.CreateDirectory(path);window.CaptureRenderedFrame()?.Save(Path.Combine(path,name+".png"));}
}
