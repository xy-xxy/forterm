using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class MonitorGaugeUiTests
{
    [AvaloniaFact] public async Task TwoHundredFiftySixCoresUseVirtualizedGaugesAndLastCoreIsReachableInNarrowPanel()
    {
        using var fixture=new TransportTests.Fixture("FIXTURE_MANY_CPUS");using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20));using var root=await SharedSshSession.ConnectAsync("127.0.0.1",fixture.Port,"synthetic","synthetic-only",(_,_)=>Task.FromResult(true),ct.Token);
        using var panel=new MonitorPanel(()=>root);var owner=new Window{Width=180,Height=433,Content=panel};owner.Show();
        try
        {
            await panel.RefreshAsync();await panel.RefreshAsync();owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
            var cores=panel.GetVisualDescendants().OfType<ListBox>().Single(l=>AutomationProperties.GetName(l)=="逐核CPU仪表");Assert.Equal(128,cores.Items.Count);Assert.Empty(panel.GetVisualDescendants().OfType<ProgressBar>());
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="25.0%");
            cores.ScrollIntoView(cores.Items.Count-1);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!cores.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="cpu255")){await Task.Delay(10,timeout.Token);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();}
            Assert.InRange(cores.GetVisualDescendants().Count(c=>c.GetType().Name=="SegmentedUsage"),2,40);
            var scroll=panel.GetVisualDescendants().OfType<ScrollViewer>().Single(c=>AutomationProperties.GetName(c)=="资源监控滚动区");var point=cores.TranslatePoint(default,(Control)scroll.Content!)!.Value;scroll.Offset=new Vector(0,point.Y);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"monitor-256-cores-last-180.png"));
            await panel.RefreshAsync(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(cores.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "cpu255");
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"monitor-256-cores-last-after-refresh-180.png"));
            Assert.Equal(1,fixture.Stats().GetProperty("auth").GetInt32());Assert.Equal(3,fixture.Stats().GetProperty("execs").GetInt32());
        }
        finally{owner.Close();}
    }
}
