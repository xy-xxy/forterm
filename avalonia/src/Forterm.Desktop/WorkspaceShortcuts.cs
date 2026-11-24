using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
namespace Forterm.Desktop;
public static class WorkspaceShortcuts
{
    public static void Install(Window window, Action<Key> run)
    {
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.KeyModifiers is not (KeyModifiers.Control | KeyModifiers.Shift) and not (KeyModifiers.Meta | KeyModifiers.Shift)) return;
            if (e.Key is not (Key.B or Key.L or Key.N or Key.T or Key.W or Key.PageUp or Key.PageDown or Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.F1)) return;
            e.Handled = true; run(e.Key);
        }, RoutingStrategies.Tunnel);
    }
    public static void ShowHelp(Window owner)
    {
        var window = new Window { Title = "快捷键", Width = 520, Height = 480, MinWidth = 380, MinHeight = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(20) }; var close = UI.Button("关闭", () => window.Close()); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = new TextBlock { Text = "Windows/Linux使用Ctrl+Shift；Mac也可使用⌘+Shift\n\nB  展开/收起导航\nL  连接库\nN  新建连接\nT  新建相同会话（共享认证）\nW  关闭当前工作区（先检查未保存草稿）\nPageUp / PageDown  前后会话\n1 / 2 / 3 / 4  终端 / 文件 / 编辑器 / 监控\nF1  本帮助\n\n文件区聚焦：Ctrl/⌘+C、X、V复制/剪切/粘贴\n分隔线聚焦：方向键调整；双击重置比例\n终端与编辑器保留各自的复制、粘贴、Tab等按键。", TextWrapping = Avalonia.Media.TextWrapping.Wrap } }); window.Content = root; window.ShowDialog(owner);
    }
}
