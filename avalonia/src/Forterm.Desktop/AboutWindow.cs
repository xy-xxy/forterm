using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Forterm.Desktop;
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "关于 Forterm"; Width = 560; Height = 510; MinWidth = 380; MinHeight = 280; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string version = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var text = new StackPanel { Spacing = 14, Margin = new Thickness(24), Children = {
            new TextBlock { Text = "Forterm", FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#49BA7A") }, UI.Label(version, true),
            UI.Label("终端 · 文件传输 · 在线编辑 · 资源监控"),
            UI.Label("应用信息"), UI.Label("C# / Avalonia 原生界面，所需.NET运行时随应用提供。旧版本与各版本配置独立保留。", true),
            UI.Label("验证说明"), UI.Label("软件回环与界面测试记录随包提供。Windows实机、输入法、系统文件选择器、真实凭据库及生产堡垒机仍需现场确认。有限的Mac空窗检查不代表所有连接能力已通过。", true),
            UI.Label("连接与文件边界"), UI.Label("可用功能取决于服务器权限。FTP会话仅提供文件能力；终端和资源监控需要SSH会话。未确认安全覆盖时保留原文件，选择另存新名。", true),
            UI.Label("详细验证记录与待现场项目见随包更新说明和验收清单。", true)
        } };
        var body = new DockPanel(); var close = UI.Button("关闭", () => Close()); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(24, 8, 24, 20); DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close); body.Children.Add(new ScrollViewer { Content = text }); Content = body;
    }
}
