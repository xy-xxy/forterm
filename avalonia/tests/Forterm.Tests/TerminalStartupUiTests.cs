using System.Runtime.ExceptionServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Xunit;
namespace Forterm.Tests;

public sealed class TerminalStartupUiTests
{
    static string Text(TerminalControl control)
    {
        var model=control.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault()?.Terminal;
        if(model==null)return "";
        var text=new System.Text.StringBuilder();
        for(int i=0;i<model.Buffer.Length;i++){var line=model.Buffer.GetLine(i);if(i>0&&line?.IsWrapped!=true)text.Append('\n');text.Append(line?.TranslateToString(true)??"");}
        return text.ToString();
    }
    static async Task Until(Window w,Func<bool> check)
    {
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while(!check()){await Task.Delay(15,ct.Token);w.UpdateLayout();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);}
    }
    static string OwnedDirectory(){string p=Path.Combine(Path.GetTempPath(),"forterm-startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task NormalLocalButtonWaitsForInitializationAndLaunchesOnce(int scenario)
    {
        if(!OperatingSystem.IsMacOS())return;
        string dir=OwnedDirectory(), count=Path.Combine(dir,"starts"),left="OWNED_LEFT_"+scenario,ready="OWNED_READY_"+scenario;
        // Actual newline inside PROMPT, prepared before launch. No interactive command injection.
        string fixture="PROMPT='%F{blue}"+left+"-中文-long-prompt-abcdefghijklmnopqrstuvwxyz%f\nSECOND> '\nRPROMPT='%F{green}OWNED_RIGHT%f'\nHISTFILE='"+Path.Combine(dir,"history")+"'\nSAVEHIST=0\nprint -r -- $$ >> '"+count+"'\nprint -r -- "+ready+"\n";
        Assert.Contains("%f\nSECOND",fixture);Assert.DoesNotContain("\\n",fixture);File.WriteAllText(Path.Combine(dir,".zshrc"),fixture);
        string? previous=Environment.GetEnvironmentVariable("ZDOTDIR");Environment.SetEnvironmentVariable("ZDOTDIR",dir);
        var errors=new List<string>();EventHandler<FirstChanceExceptionEventArgs> handler=(_,e)=>{if(e.Exception is NullReferenceException && e.Exception.StackTrace?.Contains("Iciclecreek.Terminal")==true)lock(errors)errors.Add(e.Exception.StackTrace);};AppDomain.CurrentDomain.FirstChanceException+=handler;
        var w=new MainWindow(null);w.Show();w.UpdateLayout();Dispatcher.UIThread.RunJobs();
        try
        {
            // Layout only the existing library. The new pane has NOT been laid out when the real handler starts it.
            w.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"本地终端")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(w,()=>w.Workspaces.Count==1&&Text(w.Workspaces[0].Terminal).Contains(ready));
            var pane=w.Workspaces[0];await Task.Delay(120);Dispatcher.UIThread.RunJobs();
            foreach(var size in new[]{new Size(1300,850),new Size(700,433),new Size(1200,800)})
            {
                w.Width=size.Width;w.Height=size.Height;w.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(160);w.UpdateLayout();Dispatcher.UIThread.RunJobs();
                string buffer=Text(pane.Terminal);Assert.DoesNotContain("Error launching process",buffer);Assert.Equal(1,buffer.Split(left).Length-1);
            }
            Assert.Empty(errors);Assert.Single(File.ReadAllLines(count));Assert.False(pane.Terminal.AutoStartOnLoad);
            w.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"normal-local-startup-{scenario}.png"));
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,$"normal-local-startup-{scenario}.json"),JsonSerializer.Serialize(new{realButton=true,preLayoutNewPane=false,startCount=File.ReadAllLines(count).Length,exceptions=errors,actualMultilineFixture=true,buffer=Text(pane.Terminal),nativeWindow=false}));
        }
        finally{w.Close();AppDomain.CurrentDomain.FirstChanceException-=handler;Environment.SetEnvironmentVariable("ZDOTDIR",previous);Directory.Delete(dir,true);}
    }

    [AvaloniaFact]
    public async Task ViewConcurrentEarlyLaunchAndAutomaticLoadShareOneProcess()
    {
        if(!OperatingSystem.IsMacOS())return;
        string dir=OwnedDirectory(),count=Path.Combine(dir,"starts");
        var view=new TerminalView{Process="/bin/zsh",ProcessArgs=new[]{"-f","-c","print -r -- $$ >> '"+count+"'; sleep 20"}};
        var first=view.LaunchProcess();var second=view.LaunchProcess();Assert.Same(first,second);Assert.False(first.IsCompleted);
        var w=new Window{Content=view};
        try{w.Show();await Until(w,()=>first.IsCompleted&&File.Exists(count));await first;await Task.Delay(120);Assert.Single(File.ReadAllLines(count));}
        finally{view.Dispose();w.Close();Directory.Delete(dir,true);}
    }

    [AvaloniaFact]
    public async Task DisposeBeforeInitializationCancelsPendingLaunchWithoutSpawning()
    {
        string dir=OwnedDirectory(),marker=Path.Combine(dir,"should-not-exist");
        var view=new TerminalView{Process="/bin/zsh",ProcessArgs=new[]{"-f","-c","touch '"+marker+"'"}};
        var pending=view.LaunchProcess();Assert.False(pending.IsCompleted);view.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));Assert.False(File.Exists(marker));Directory.Delete(dir,true);
    }

    [AvaloniaFact]
    public async Task MissingExecutableFailureRemainsVisible()
    {
        var control=SessionPane.CreateTerminal();control.Process=Path.Combine(Path.GetTempPath(),"missing-forterm-"+Guid.NewGuid().ToString("N"));
        var w=new Window{Content=control};var previous=Console.Error;using var diagnostics=new StringWriter();Console.SetError(diagnostics);
        try
        {
            // Same early call, with a genuine spawn failure after initialization.
            var pending=control.LaunchProcess();w.Show();await Until(w,()=>pending.IsCompleted);await pending;await Until(w,()=>Text(control).Contains("Error launching process")||Text(control).Contains("Process exited with code:"));
            // Unix exec failure is reported by the child as exit 2, not thrown by SpawnAsync.
            string buffer=Text(control);Assert.True(buffer.Contains("Error launching process")||buffer.Contains("Process exited with code: 2"),buffer);
            if(diagnostics.ToString().Length>0){Assert.Contains("Terminal launch failed:",diagnostics.ToString());Assert.Contains("LaunchProcessCoreAsync",diagnostics.ToString());}
            Assert.DoesNotContain(control.Process,diagnostics.ToString());
        }
        finally{Console.SetError(previous);control.Dispose();w.Close();}
    }
}
