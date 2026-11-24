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
using Xunit;
namespace Forterm.Tests;
public sealed class NarrowQueueUiTests
{
    [AvaloniaFact] public async Task NarrowTwoLineRowsKeepTwentySixDpHitTargetsAndRealMouseCancel()
    {
        using var queue=new TransferQueue();var owner=new Window{Width=170,Height=300};using var panel=new TransferPanel(owner,queue);owner.Content=panel;owner.Show();
        queue.Add("首任务","合成来源",async(_,ct)=>await Task.Delay(Timeout.Infinite,ct));var target=queue.Add("窄栏任务2长文件名","合成来源 /long/path",(_,_)=>Task.CompletedTask);queue.Add("第三任务","合成来源",(_,_)=>Task.CompletedTask);
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));Button? cancel=null;
            while(cancel==null){await Task.Delay(10,timeout.Token);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();cancel=panel.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>AutomationProperties.GetName(b)=="取消任务 窄栏任务2长文件名");}
            foreach(var button in panel.GetVisualDescendants().OfType<Button>().Where(b=>AutomationProperties.GetName(b)?.StartsWith("任务详情 ")==true||AutomationProperties.GetName(b)?.StartsWith("取消任务 ")==true))
            {Assert.Equal(26,button.Bounds.Width);Assert.Equal(26,button.Bounds.Height);var point=button.TranslatePoint(default,owner)!.Value;Assert.True(point.X>=0&&point.X+26<=owner.Bounds.Width);Assert.NotNull(ToolTip.GetTip(button));}
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"queue-narrow-170.png"));
            var center=cancel.TranslatePoint(new Point(13,13),owner)!.Value;owner.MouseDown(center,MouseButton.Left,RawInputModifiers.None);owner.MouseUp(center,MouseButton.Left,RawInputModifiers.None);Assert.Equal(TransferState.Cancelled,(await target.Completion.WaitAsync(timeout.Token)).State);
            var detail=panel.GetVisualDescendants().OfType<Button>().First(b=>AutomationProperties.GetName(b)?.StartsWith("任务详情 ")==true);detail.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(20);var dialog=owner.OwnedWindows.Single();dialog.Width=300;dialog.Height=200;dialog.UpdateLayout();Dispatcher.UIThread.RunJobs();var close=dialog.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"关闭"));var at=close.TranslatePoint(default,dialog)!.Value;Assert.True(at.Y+close.Bounds.Height<=dialog.Bounds.Height);dialog.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,"queue-details-small.png"));dialog.Close();
        }
        finally{owner.Close();}
    }
}
