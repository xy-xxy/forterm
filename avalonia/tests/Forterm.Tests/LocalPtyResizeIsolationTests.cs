using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Porta.Pty;
using Xunit;
namespace Forterm.Tests;
public sealed class LocalPtyResizeIsolationTests
{
    static string Text(SessionPane pane)
    {
        var text=new System.Text.StringBuilder();for(int i=0;i<pane.Terminal.Terminal.Buffer.Length;i++){var line=pane.Terminal.Terminal.Buffer.GetLine(i);if(i>0&&line?.IsWrapped!=true)text.Append('\n');text.Append(line?.TranslateToString(true)??"");}return text.ToString();
    }
    static async Task Until(Func<bool> check){using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));while(!check()){await Task.Delay(10,ct.Token);Dispatcher.UIThread.RunJobs();}}
    static void Layout(Window w){w.UpdateLayout();Dispatcher.UIThread.RunJobs();w.UpdateLayout();}
    static void Add(MainWindow w,SessionPane p,string title){typeof(MainWindow).GetMethod("AddPane",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,[p,title]);Layout(w);}
    static Dictionary<string,string> Env(string dir)=>new(){{"ZDOTDIR",dir},{"TERM","xterm-256color"},{"PROMPT","OWNED> "},{"RPROMPT",""},{"HISTFILE",Path.Combine(dir,"owned-history")},{"SAVEHIST","0"}};
    static async Task Probe(MainWindow w,SessionPane pane,string dir,string nonce)
    {
        File.WriteAllText(Path.Combine(dir,"token"),nonce+"\n");File.WriteAllText(Path.Combine(dir,"probe.zsh"),"stty size > '"+Path.Combine(dir,"size")+"'; cat '"+Path.Combine(dir,"token")+"'\n");
        var view=pane.Terminal.GetVisualDescendants().OfType<TerminalView>().Single();view.Focus();w.KeyTextInput("source '"+Path.Combine(dir,"probe.zsh")+"'");w.KeyPress(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);w.KeyRelease(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);try{await Until(()=>Text(pane).Contains(nonce));}catch{File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,"owned-local-probe-failure.txt"),$"live={pane.Terminal.IsLive}; pid={pane.Terminal.Pid}; focused={view.IsFocused}; sizeFile={File.Exists(Path.Combine(dir,"size"))}\n"+Text(pane));w.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"owned-local-probe-failure.png"));throw;}
        int[] dimensions=File.ReadAllText(Path.Combine(dir,"size")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();Assert.Equal(pane.Terminal.Terminal.Rows,dimensions[0]);Assert.Equal(pane.Terminal.Terminal.Cols,dimensions[1]);
    }
    [AvaloniaFact] public async Task ActualLocalLaunchSettlesToFinalLayoutAndAcceptsInputAfterFontZoom()
    {
        if(!OperatingSystem.IsMacOS())return;string dir=Path.Combine(Path.GetTempPath(),"forterm-launch-resize-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var window=new MainWindow(null,new CredentialStoreTests.MemoryVault()){Width=1300,Height=850};window.Show();var pane=new SessionPane(window,null);Add(window,pane,"Owned launch resize");
        try
        {
            pane.Terminal.Process="/bin/zsh";pane.Terminal.ProcessArgs=new[]{"-f","-i"};pane.Terminal.StartingDirectory=dir;pane.Terminal.EnvironmentVariables=Env(dir);
            var launched=pane.Terminal.LaunchProcess();window.Width=700;window.Height=433;Layout(window);await launched;await Until(()=>Text(pane).Contains("OWNED>"));
            foreach(var size in new[]{new Size(700,433),new Size(1300,850),new Size(850,600)}){window.Width=size.Width;window.Height=size.Height;Layout(window);await Task.Delay(80);}
            var view=pane.Terminal.GetVisualDescendants().OfType<TerminalView>().Single();view.Zoom=1.3;Layout(window);await Task.Delay(120);string nonce="CHILD_OUTPUT_"+Guid.NewGuid().ToString("N");await Probe(window,pane,dir,nonce);Assert.Equal(1,Text(pane).Split(nonce).Length-1);
            await Task.Delay(120);Dispatcher.UIThread.RunJobs();window.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"owned-local-launch-resize-zoom.png"));
        }
        finally{pane.Dispose();window.Close();Directory.Delete(dir,true);}
    }
    [AvaloniaFact] public async Task OneLayoutTransactionCommitsOnlyItsFinalGridToRealPty()
    {
        if(!OperatingSystem.IsMacOS())return;string dir=Path.Combine(Path.GetTempPath(),"forterm-layout-commit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var w=new MainWindow(null,new CredentialStoreTests.MemoryVault()){Width=1200,Height=800};w.Show();var pane=new SessionPane(w,null);Add(w,pane,"Owned atomic layout");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var native=await PtyProvider.SpawnAsync(new PtyOptions{App="/bin/zsh",CommandLine=["-f","-i"],Cols=80,Rows=24,Cwd=dir,Environment=Env(dir)},ct.Token);var trace=new ObservedPty(native);pane.Terminal.AttachConnection(trace);await Until(()=>Text(pane).Contains("OWNED>"));Layout(w);
        try
        {
            int before=trace.Sizes.Count;
            foreach(int width in new[]{920,760,1050,840}){w.Width=width;w.Height=610;w.UpdateLayout();}
            Assert.Equal(before,trace.Sizes.Count);Dispatcher.UIThread.RunJobs();w.UpdateLayout();Dispatcher.UIThread.RunJobs();
            Assert.Single(trace.Sizes.Skip(before));Assert.Equal((pane.Terminal.Terminal.Cols,pane.Terminal.Terminal.Rows),trace.Sizes.Last());
            await Probe(w,pane,dir,"ATOMIC_"+Guid.NewGuid().ToString("N"));
        }
        finally{try{native.Kill();}catch{}pane.Dispose();w.Close();Directory.Delete(dir,true);}
    }
    [AvaloniaFact] public async Task HiddenTerminalIsNotResizedAndTwoRealPtysNeverReceiveEachOthersInput()
    {
        if(!OperatingSystem.IsMacOS())return;string dir=Path.Combine(Path.GetTempPath(),"forterm-two-pty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string a=Path.Combine(dir,"a"),b=Path.Combine(dir,"b");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
        var window=new MainWindow(null,new CredentialStoreTests.MemoryVault()){Width=1200,Height=800};window.Show();var first=new SessionPane(window,null);Add(window,first,"Owned first");using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var native1=await PtyProvider.SpawnAsync(new PtyOptions{App="/bin/zsh",CommandLine=["-f","-i"],Cols=80,Rows=24,Cwd=a,Environment=Env(a)},cts.Token);var trace1=new ObservedPty(native1);first.Terminal.AttachConnection(trace1);await Until(()=>Text(first).Contains("OWNED>"));
        var second=new SessionPane(window,null);Add(window,second,"Owned second");using var native2=await PtyProvider.SpawnAsync(new PtyOptions{App="/bin/zsh",CommandLine=["-f","-i"],Cols=80,Rows=24,Cwd=b,Environment=Env(b)},cts.Token);var trace2=new ObservedPty(native2);second.Terminal.AttachConnection(trace2);await Until(()=>Text(second).Contains("OWNED>"));
        try
        {
            Assert.NotEqual(native1.Pid,native2.Pid);int before=trace1.Sizes.Count;
            foreach(var size in new[]{new Size(700,433),new Size(1300,850),new Size(900,650)}){window.Width=size.Width;window.Height=size.Height;Layout(window);await Task.Delay(100,cts.Token);}
            Assert.Equal(before,trace1.Sizes.Count);Assert.Equal((second.Terminal.Terminal.Cols,second.Terminal.Terminal.Rows),trace2.Sizes.Last());string token2="SECOND_ONLY_"+Guid.NewGuid().ToString("N");await Probe(window,second,b,token2);Assert.DoesNotContain(token2,Text(first));
            var tabs=window.GetVisualDescendants().OfType<TabControl>().Single(t=>Avalonia.Automation.AutomationProperties.GetName(t)=="会话标签");tabs.SelectedIndex=0;Layout(window);await Task.Delay(100,cts.Token);string token1="FIRST_ONLY_"+Guid.NewGuid().ToString("N");await Probe(window,first,a,token1);Assert.DoesNotContain(token1,Text(second));Assert.True(first.Terminal.IsLive&&second.Terminal.IsLive);
            await Task.Delay(120);Dispatcher.UIThread.RunJobs();window.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"owned-two-pty-returned-first.png"));
        }
        finally{try{native1.Kill();native2.Kill();}catch{}first.Dispose();second.Dispose();window.Close();Directory.Delete(dir,true);}
    }
}
