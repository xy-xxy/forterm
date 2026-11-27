using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Services;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Studio 0.4.0-alpha.14.</summary>
public sealed class Alpha14Tests
{
    static void Settle(Window window) { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }

    /// <summary>Menus filled as they open are drawn with their entries, every time: in Avalonia 12 what was added to a menu's
    /// Items while it opened was not shown, and "排序和显示" and "关联文件会话" opened two pixels wide (on Windows: nothing).</summary>
    [AvaloniaFact]
    public async Task MenusFilledAsTheyOpenAreDrawn()
    {
        using var shell = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "终端", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{shell.Port}"])))!;
            var end = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < end && !w.HasFiles) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
            Settle(window);
            void Press(Control c) { var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value; window.MouseMove(at); window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window); }
            var view = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FileViewButton" && b.IsEffectivelyVisible);
            for (int round = 0; round < 2; round++)
            {
                Press(view);
                var menu = (MenuFlyout)view.Flyout!;
                Assert.True(menu.IsOpen);
                var shown = Menus.Shown(menu);
                Assert.Equal(Appearance.FileSorts.Length + 3, shown.Count);
                Assert.Equal("按" + Appearance.FileSorts[0].Title, Menus.Text(shown[0]));
                Assert.True(Menus.Presenter(menu)!.Bounds.Width > 100, "the menu is drawn");
                menu.Hide();
                Settle(window);
            }
            foreach (var x in h.Model.Workspaces.ToList()) h.Model.Close(x);
            Settle(window);
        }
        finally { window.Close(); }
    }
}
