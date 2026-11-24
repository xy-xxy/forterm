using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
namespace Forterm.Desktop;

public sealed class PreferencesWindow : Window
{
    public PreferencesWindow(UserPreferences current, PreferenceStore store, Action<UserPreferences> apply)
    {
        Title = "偏好设置"; Width = 620; Height = 540; MinWidth = 400; MinHeight = 300; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var theme = new ComboBox { ItemsSource = new[] { "深色", "浅色", "跟随系统" }, SelectedIndex = current.Appearance == "dark" ? 0 : current.Appearance == "light" ? 1 : 2 };
        DropDownMotion.Attach(theme);
        var terminalFont = new NumericUpDown { Minimum = 10, Maximum = 30, Value = (decimal)current.TerminalFontSize }; var uiFont = new NumericUpDown { Minimum = 12, Maximum = 20, Value = (decimal)current.UiFontSize };
        var auto = new CheckBox { Content = "连接成功后自动收起连接导航", IsChecked = current.AutoCollapseNavigation }; var enabled = new CheckBox { Content = "启用SSH协议保活", IsChecked = current.KeepAliveEnabled }; var seconds = new NumericUpDown { Minimum = 5, Maximum = 600, Value = current.KeepAliveSeconds }; var sftp = new CheckBox { Content = "对已打开SFTP通道进行只读探测", IsChecked = current.SftpProbeEnabled };
        var message = UI.Label("保活只发送协议消息，不向终端输入命令或按键。保活设置用于之后新建的连接；既有会话保持建立时快照。", true);
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20), Children = { UI.Label("主题"), theme, UI.Label("终端字号"), terminalFont, UI.Label("界面字号"), uiFont, auto, enabled, UI.Label("心跳间隔（秒，5–600）"), seconds, sftp, message } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20) }; actions.Children.Add(UI.Button("取消", () => Close())); actions.Children.Add(UI.Button("保存设置", () => { try { var next = current with { Appearance = theme.SelectedIndex == 0 ? "dark" : theme.SelectedIndex == 1 ? "light" : "system", TerminalFontSize = (double)(terminalFont.Value ?? 14), UiFontSize = (double)(uiFont.Value ?? 14), AutoCollapseNavigation = auto.IsChecked == true, KeepAliveEnabled = enabled.IsChecked == true, KeepAliveSeconds = (int)(seconds.Value ?? 15), SftpProbeEnabled = sftp.IsChecked == true }; store.Save(next); apply(next); Close(); } catch (Exception e) { message.Text = e.Message; } }, true));
        var root = new DockPanel(); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions); root.Children.Add(new ScrollViewer { Content = body }); Content = root;
    }
}
