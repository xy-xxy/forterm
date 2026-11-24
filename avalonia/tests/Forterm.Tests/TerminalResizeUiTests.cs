using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Porta.Pty;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Xunit;
namespace Forterm.Tests;
public sealed class TerminalResizeUiTests
{
    static string Text(TerminalControl control)
    {
        var text=new System.Text.StringBuilder();for(int i=0;i<control.Terminal.Buffer.Length;i++){var line=control.Terminal.Buffer.GetLine(i);if(i>0&&line?.IsWrapped!=true)text.Append('\n');text.Append(line?.TranslateToString(true)??"");}return text.ToString();
    }
    static async Task Until(Func<bool> check){using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));while(!check()){await Task.Delay(10,ct.Token);Dispatcher.UIThread.RunJobs();}}
    static int Count(string text,string value)=>text.Split(value,StringSplitOptions.None).Length-1;
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task OwnedZshColoredLeftRightPromptAcrossNormalMainWindowShrinkAndGrow(int attempt)
    {
        if(!OperatingSystem.IsMacOS())return;
        string dir=Path.Combine(Path.GetTempPath(),"forterm-resize-owned-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string left="LEFT_"+Guid.NewGuid().ToString("N")[..8],right="RIGHT_"+Guid.NewGuid().ToString("N")[..8],ready="READY_"+Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(dir,"prompt.zsh"),"PROMPT='%F{blue}"+left+"-\uE0B0-中文-长路径/own/very/long/deep/path/segment/one/two/three%f\nSECOND> '; RPROMPT='%F{green}"+right+"%f'; print -r -- "+ready+"\n");
        var window=new MainWindow(null,new CredentialStoreTests.MemoryVault()){Width=1300,Height=850};window.Show();var pane=new SessionPane(window,null);
        typeof(MainWindow).GetMethod("AddPane",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[pane,"Owned isolated zsh -f"]);window.UpdateLayout();Dispatcher.UIThread.RunJobs();
        var view=pane.Terminal.GetVisualDescendants().OfType<TerminalView>().Single();using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var native=await PtyProvider.SpawnAsync(new PtyOptions{App="/bin/zsh",CommandLine=["-f","-i"],Cols=80,Rows=24,Cwd=dir,Environment=new Dictionary<string,string>{{"ZDOTDIR",dir},{"TERM","xterm-256color"},{"PROMPT","OWNED_BOOT> "},{"RPROMPT",""},{"HISTFILE",Path.Combine(dir,"owned-history")},{"SAVEHIST","0"}}},cts.Token);
        var pty=new ObservedPty(native); pane.Terminal.AttachConnection(pty);
        Assert.Equal((pane.Terminal.Terminal.Cols,pane.Terminal.Terminal.Rows),pty.Sizes.Last());
        var checkpoints=new List<object>();
        try
        {
            await Until(()=>Text(pane.Terminal).Contains("OWNED_BOOT>"));
            await pty.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("source '"+Path.Combine(dir,"prompt.zsh")+"'\r"),cts.Token);await Until(()=>Text(pane.Terminal).Contains(ready)&&Text(pane.Terminal).Contains(left));await Task.Delay(150,cts.Token);
            Assert.False(pane.Terminal.Terminal.Options.ReflowOnResize);
            int initial=Count(Text(pane.Terminal),left); Assert.Equal(1,initial); int index=0;
            foreach(var size in new[]{new Size(900,650),new Size(700,433),new Size(1200,800),new Size(700,433),new Size(1300,850),new Size(700,433),new Size(1300,850)})
            {
                window.Width=size.Width;window.Height=size.Height;window.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(180,cts.Token);window.UpdateLayout();Dispatcher.UIThread.RunJobs();
                string text=Text(pane.Terminal);checkpoints.Add(new{size=size.ToString(),font=pane.Terminal.FontFamily.Name,viewFont=view.FontFamily.Name,charWidth=view.CharWidth,charHeight=view.CharHeight,cols=pane.Terminal.Terminal.Cols,rows=pane.Terminal.Terminal.Rows,cursorX=pane.Terminal.Terminal.Buffer.X,cursorY=pane.Terminal.Terminal.Buffer.Y,leftCount=Count(text,left),rightCount=Count(text,right),buffer=text});
                window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"owned-zsh-resize-case{attempt}-{index++}-{size.Width}.png"));
            }
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,$"owned-zsh-resize-observations-case{attempt}.json"),JsonSerializer.Serialize(checkpoints,new JsonSerializerOptions{WriteIndented=true}));
            pane.ToggleCapability(2);pane.ToggleCapability(3);
            var placement=pane.GetVisualDescendants().OfType<ComboBox>().Single(c=>Avalonia.Automation.AutomationProperties.GetName(c)=="工作区排列");
            foreach(int arrangement in (attempt%2==0?new[]{0,1,2}:new[]{2,0,1,2}))
            {
                placement.SelectedIndex=arrangement;window.Width=arrangement==1?900:1300;window.Height=arrangement==1?700:850;window.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(180,cts.Token);
                if(Count(Text(pane.Terminal),left)!=initial){File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,$"owned-four-panel-failure-case{attempt}.json"),JsonSerializer.Serialize(new{arrangement,initial,actual=Count(Text(pane.Terminal),left),cols=pane.Terminal.Terminal.Cols,rows=pane.Terminal.Terminal.Rows,cursorX=pane.Terminal.Terminal.Buffer.X,cursorY=pane.Terminal.Terminal.Buffer.Y,requests=pty.Sizes.Select(s=>new{s.Cols,s.Rows}),buffer=Text(pane.Terminal)}));window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"owned-four-panel-failure-case{attempt}.png"));}
                Assert.Equal(initial,Count(Text(pane.Terminal),left));Assert.Equal((pane.Terminal.Terminal.Cols,pane.Terminal.Terminal.Rows),pty.Sizes.Last());
                window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"owned-zsh-resize-four-panels-case{attempt}-{arrangement}.png"));
            }
            pane.ToggleCapability(2);pane.ToggleCapability(3);
            // Rapid layout churn must settle at the latest real PTY dimensions.
            for(int i=0;i<24;i++){window.Width=700+(i%7)*80;window.Height=433+(i%5)*60;window.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(8,cts.Token);}
            window.Width=1100;window.Height=750;window.UpdateLayout();Dispatcher.UIThread.RunJobs();await Task.Delay(200,cts.Token);
            Assert.Equal((pane.Terminal.Terminal.Cols,pane.Terminal.Terminal.Rows),pty.Sizes.Last()); Assert.All(pty.Sizes,s=>{Assert.True(s.Cols>0);Assert.True(s.Rows>0);});
            string output="OUTPUT_"+Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(dir,"token"),output+"\n");
            File.WriteAllText(Path.Combine(dir,"probe.zsh"),"stty size > '"+Path.Combine(dir,"actual-size")+"'; cat '"+Path.Combine(dir,"token")+"'\n");
            view.Focus();window.KeyTextInput("source '"+Path.Combine(dir,"probe.zsh")+"'");window.KeyPress(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);window.KeyRelease(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);await Until(()=>Text(pane.Terminal).Contains(output));
            var actual=File.ReadAllText(Path.Combine(dir,"actual-size")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();Assert.Equal(pane.Terminal.Terminal.Rows,actual[0]);Assert.Equal(pane.Terminal.Terminal.Cols,actual[1]);
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,$"owned-local-final-size-case{attempt}.json"),JsonSerializer.Serialize(new{actualRows=actual[0],actualCols=actual[1],modelRows=pane.Terminal.Terminal.Rows,modelCols=pane.Terminal.Terminal.Cols,requests=pty.Sizes.Select(s=>new{s.Cols,s.Rows}),nonEchoOutput=output}));
            Assert.Equal(1,Count(Text(pane.Terminal),output));
            window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            int outputRow=Enumerable.Range(0,pane.Terminal.Terminal.Buffer.Length).Single(i=>pane.Terminal.Terminal.Buffer.GetLine(i)?.TranslateToString(true).Contains(output)==true);
            int col=pane.Terminal.Terminal.Buffer.GetLine(outputRow)!.TranslateToString(true).IndexOf(output,StringComparison.Ordinal);int screenRow=outputRow-view.ViewportY;var origin=view.TranslatePoint(default,window)!.Value;
            var start=origin+new Vector((col+.5)*view.CharWidth,(screenRow+.5)*view.CharHeight);var end=origin+new Vector((col+output.Length+.01)*view.CharWidth,(screenRow+.5)*view.CharHeight);
            window.MouseDown(start,MouseButton.Left,RawInputModifiers.None);window.MouseMove(end,RawInputModifiers.LeftMouseButton);window.MouseUp(end,MouseButton.Left,RawInputModifiers.None);Assert.True(await pane.Terminal.CopyAsync(),$"selection={pane.Terminal.Terminal.Selection.HasSelection}; row={outputRow}; viewport={view.ViewportY}; screenRow={screenRow}; view={view.Bounds}; start={start}; end={end}");Assert.Equal(output,(await window.Clipboard!.TryGetTextAsync())?.TrimEnd());

            Assert.Equal(initial,Count(((JsonElement)JsonSerializer.SerializeToElement(checkpoints[^1])).GetProperty("buffer").GetString()!,left));
        }
        finally{try{pty.Kill();}catch{}pane.Dispose();window.Close();Directory.Delete(dir,true);}
    }
}
