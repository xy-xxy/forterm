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
public sealed class MainLayoutReviewTests
{
    static async Task Until(Func<bool> check) { using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15)); while(!check()){await Task.Delay(10,ct.Token);Dispatcher.UIThread.RunJobs();} }
    static void Click(Control root,string label)=>root.GetVisualDescendants().OfType<Button>().First(b=>Equals(b.Content,label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    [AvaloniaFact] public async Task NormalMainWindowShowsExpandedWorkspaceWithoutFixtureReparenting()
    {
        using var fixture=new TransportTests.Fixture("FIXTURE_MANY_CPUS",posix:true);fixture.SeedText();string dir=Path.Combine(Path.GetTempPath(),"forterm-main-review-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);new ProfileStore(dir).Save([new("合成服务器","合成测试","127.0.0.1",fixture.Port,"synthetic")]);
        var window=new MainWindow(dir,new CredentialStoreTests.MemoryVault()){Width=1150,Height=760};window.Show();
        try
        {
            var open=window.AcceptLaunch(LaunchPlan.Parse(["-url",$"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));await Until(()=>window.OwnedWindows.Count>0);Click(window.OwnedWindows.Single(),"确认");await open;window.UpdateLayout();
            var nav=window.GetVisualDescendants().OfType<TreeView>().Single(t=>AutomationProperties.GetName(t)=="连接分组");if(!nav.IsEffectivelyVisible)Click(window,"导航");
            var pane=Assert.Single(window.Workspaces);var list=pane.GetVisualDescendants().OfType<ListBox>().Single(l=>AutomationProperties.GetName(l)=="远程文件");list.SelectedItem=list.Items.Cast<object>().Single(i=>i.ToString()!.Contains("edit.txt"));Click(pane,"编辑");await Until(()=>pane.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Any());
            pane.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Content,"资源监控")).IsChecked=true;var monitor=pane.GetVisualDescendants().OfType<MonitorPanel>().Single();await monitor.RefreshAsync();await monitor.RefreshAsync();await monitor.RefreshAsync();window.UpdateLayout();Dispatcher.UIThread.RunJobs();
            Assert.Equal(1,fixture.Stats().GetProperty("auth").GetInt32());window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"normal-main-expanded-dark.png"));
            foreach(var size in new[]{new Size(1150,760),new Size(700,433),new Size(1050,650)})
            {
                window.Width=size.Width;window.Height=size.Height;window.UpdateLayout();Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"normal-main-text-review-{size.Width}.png"));
                var textEditor=pane.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single(); Assert.True(textEditor.TextArea.TextView.Bounds.Height>=14,$"editor text viewport {textEditor.TextArea.TextView.Bounds} at {size}; " + string.Join(" | ",textEditor.TextArea.TextView.GetVisualAncestors().Take(12).Select(v=>v.GetType().Name+":"+v.Bounds)));
                var navPoint=nav.TranslatePoint(default,window)!.Value;var filePoint=list.TranslatePoint(default,window)!.Value;var termPoint=pane.Terminal.TranslatePoint(default,window)!.Value;var monitorPoint=monitor.TranslatePoint(default,window)!.Value;
                Assert.True(navPoint.X<filePoint.X&&filePoint.X<termPoint.X&&termPoint.X<monitorPoint.X,$"order {navPoint} {filePoint} {termPoint} {monitorPoint}");
                window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"normal-main-review-{size.Width}-logical.png"));
                Assert.True(monitorPoint.X+monitor.Bounds.Width<=window.Bounds.Width+1,$"monitor {monitorPoint} {monitor.Bounds} at {size}");Assert.True(list.Bounds.Width>0&&list.Bounds.Height>0,$"files {list.Bounds} at {size}");Assert.True(pane.Terminal.Bounds.Width>0&&pane.Terminal.Bounds.Height>0,$"terminal {pane.Terminal.Bounds} at {size}");
                window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"normal-main-expanded-{size.Width}-logical.png"));
                var scroll=monitor.GetVisualDescendants().OfType<ScrollViewer>().Single(c=>AutomationProperties.GetName(c)=="资源监控滚动区");scroll.Offset=new Vector(0,double.MaxValue);window.UpdateLayout();Dispatcher.UIThread.RunJobs();
                var last=monitor.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text?.StartsWith("绿：接收")==true);var end=last.TranslatePoint(default,scroll)!.Value;Assert.True(end.Y>=-1&&end.Y+last.Bounds.Height<=scroll.Bounds.Height+1);
                window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,$"normal-main-monitor-bottom-{size.Width}-logical.png"));scroll.Offset=default;
            }
            window.Width=1150;window.Height=760;Application.Current!.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"normal-main-expanded-light.png"));Application.Current.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
            var positions=new[]{("navigation",(Control)nav),("files",list),("terminal",pane.Terminal),("monitor",monitor)}.Select(p=>new{area=p.Item1,point=p.Item2.TranslatePoint(default,window),bounds=p.Item2.Bounds}).ToArray();
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath,"normal-main-positions.json"),System.Text.Json.JsonSerializer.Serialize(positions));
        }
        finally{Application.Current!.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;window.Close();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
