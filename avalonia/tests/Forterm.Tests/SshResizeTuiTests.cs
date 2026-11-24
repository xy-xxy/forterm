using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Xunit;
namespace Forterm.Tests;
public sealed class SshResizeTuiTests
{
    static string Text(SessionPane p)
    {
        var text=new System.Text.StringBuilder();for(int i=0;i<p.Terminal.Terminal.Buffer.Length;i++){var line=p.Terminal.Terminal.Buffer.GetLine(i);if(i>0&&line?.IsWrapped!=true)text.Append('\n');text.Append(line?.TranslateToString(true)??"");}return text.ToString();
    }
    static async Task Until(Func<bool> check){using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));while(!check()){await Task.Delay(10,ct.Token);Dispatcher.UIThread.RunJobs();}}
    [AvaloniaFact] public async Task SharedSshAlternateScreenReceivesFinalWindowChangeAndNonEchoReplyAfterResize()
    {
        using var fixture=new TransportTests.Fixture("FIXTURE_RESIZE_TUI");string dir=Path.Combine(Path.GetTempPath(),"forterm-ssh-resize-"+Guid.NewGuid().ToString("N"));var w=new MainWindow(dir,new CredentialStoreTests.MemoryVault()){Width=1300,Height=850};w.Show();
        try
        {
            var open=w.AcceptLaunch(LaunchPlan.Parse(["-url",$"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));await Until(()=>w.OwnedWindows.Count>0);w.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"确认")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await open;var p=Assert.Single(w.Workspaces);await Until(()=>p.Terminal.IsAlternateBuffer);
            p.ToggleCapability(2);p.ToggleCapability(3);
            foreach(var size in new[]{new Size(900,650),new Size(700,433),new Size(1300,850),new Size(700,433),new Size(1100,750)})
            {
                w.Width=size.Width;w.Height=size.Height;w.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(100);
                try{await Until(()=>Text(p).Contains($"FRAME {p.Terminal.Terminal.Cols}x{p.Terminal.Terminal.Rows}"));}catch{File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,"owned-ssh-frame-failure.txt"),$"expected {p.Terminal.Terminal.Cols}x{p.Terminal.Terminal.Rows}; alt={p.Terminal.IsAlternateBuffer}\n"+fixture.Stats().GetRawText()+"\n"+Text(p));w.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"owned-ssh-frame-failure.png"));throw;}
                var resize=fixture.Stats().GetProperty("resizes").EnumerateArray().Last();Assert.Equal(p.Terminal.Terminal.Cols,resize[0].GetInt32());Assert.Equal(p.Terminal.Terminal.Rows,resize[1].GetInt32());
            }
            var view=p.Terminal.GetVisualDescendants().OfType<TerminalView>().Single();view.Zoom=1.2;w.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(100);var markers=fixture.Stats().GetProperty("frameMarkers").EnumerateObject().ToArray();string token=Assert.Single(markers).Value.GetString()!;
            Assert.Empty(fixture.Stats().GetProperty("input").EnumerateArray());view.Focus();w.KeyTextInput("probe");await Until(()=>Text(p).Contains($"ACK {p.Terminal.Terminal.Cols}x{p.Terminal.Terminal.Rows} {token}"));Assert.True(p.HasFileCapability);Assert.Equal(1,fixture.Stats().GetProperty("auth").GetInt32());
            string sent=string.Concat(fixture.Stats().GetProperty("input").EnumerateArray().Select(e=>e.GetString()));Assert.DoesNotContain(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(token)).ToLowerInvariant(),sent);Assert.Contains("70726f6265",sent);
            await Task.Delay(120);Dispatcher.UIThread.RunJobs();w.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            w.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"owned-ssh-resize-alternate-ack.png"));
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,"owned-ssh-resize-protocol.json"),fixture.Stats().GetRawText());
        }
        finally{w.Close();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
