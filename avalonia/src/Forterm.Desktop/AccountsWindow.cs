using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class AccountsWindow : Window
{
    readonly AccountRepository repository; readonly IFilePickers pickers; readonly ListBox accounts = new(); readonly TextBlock status = UI.Label("密码和验证器key仅存系统保管库，不导出、不自动登录门户。", true);
    public AccountsWindow(AccountRepository repository, IFilePickers? pickers = null)
    {
        this.repository = repository; this.pickers = pickers ?? new NativeFilePickers("导入账户配置", "导出账户配置"); Title = "堡垒机账户与本地验证器"; Width = 800; Height = 570; MinWidth = 520; MinHeight = 340;
        var tools = new WrapPanel { Margin = new Thickness(16) }; tools.Children.Add(UI.Button("新建账户", () => Edit(null))); tools.Children.Add(UI.Button("编辑账户", () => { if (accounts.SelectedItem is BastionAccount a) Edit(a); })); tools.Children.Add(UI.Button("本地验证码", ShowCode)); tools.Children.Add(UI.Button("删除账户", Delete)); tools.Children.Add(UI.Button("导出无秘密配置", Export));
        var root = new DockPanel(); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools); var foot = new Border { Padding = new Thickness(16), Child = status }; DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot); root.Children.Add(accounts); Content = root; Refresh();
    }
    void Refresh() { try { accounts.ItemsSource = repository.Load(); } catch (Exception e) { status.Text = e.Message; } }
    async void Edit(BastionAccount? account) { var result = await new AccountEditWindow(repository, account).ShowDialog<BastionAccount?>(this); if (result != null) { Refresh(); accounts.SelectedItem = result; status.Text = "账户元数据与系统秘密已确认保存"; } }
    async void ShowCode()
    {
        if (accounts.SelectedItem is not BastionAccount account) { status.Text = "请选择已启用验证器的账户"; return; }
        try { var current = await repository.ReadTotpAsync(account, CancellationToken.None); try { await new TotpWindow(current.Account, current.Configuration).ShowDialog(this); } finally { CryptographicOperations.ZeroMemory(current.Configuration.Secret); } }
        catch (Exception e) { status.Text = e.Message; }
    }
    async void Delete()
    {
        if (accounts.SelectedItem is not BastionAccount account) return;
        if (!await UI.Confirm(this, "删除堡垒机账户？", account.Name + " · " + account.Username + "\n同时删除本应用保存的密码与验证器key，不访问门户。")) return;
        try { await repository.DeleteAsync(account, CancellationToken.None); Refresh(); status.Text = "账户及应用保管库秘密已移除"; } catch (Exception e) { status.Text = e.Message; }
    }
    async void Export()
    {
        try
        {
            string json = repository.ExportMetadata(); string? path = await pickers.DownloadAsync(this, "forterm-accounts.json"); if (path == null) return;
            bool saved = await LocalTextFiles.WriteAsync(path, json, (target, ct) => UI.Confirm(this, "替换账户元数据导出？", target.Path + "\n不含密码、验证器key或临时票据。", ct), CancellationToken.None); status.Text = saved ? "已导出非秘密配置，验证器标记已停用" : "已取消导出";
        }
        catch (Exception e) { status.Text = e.Message; }
    }
}
public sealed class AccountEditWindow : Window
{
    readonly TextBox name = new() { MaxLength = 256 }, portal = new() { MaxLength = 4096 }, username = new() { MaxLength = 1024 }, password = new() { PasswordChar = '●', MaxLength = 32768 }, seed = new() { PasswordChar = '●', MaxLength = 4096 };
    readonly CheckBox clearPassword = new() { Content = "清除系统保存密码" }, clearSeed = new() { Content = "清除验证器key" }, enabled = new() { Content = "启用本地TOTP验证码" };
    readonly ComboBox algorithm = new() { ItemsSource = new[] { "SHA1", "SHA256", "SHA512" }, SelectedIndex = 0 }, digits = new() { ItemsSource = new[] { "6", "8" }, SelectedIndex = 0 };
    readonly NumericUpDown period = new() { Minimum = 5, Maximum = 300, Value = 30 }; readonly TextBlock status = UI.Label("保存按钮明确写系统保管库；失败保留输入，不做明文回退。", true); bool busy;
    public AccountEditWindow(AccountRepository repository, BastionAccount? previous = null)
    {
        Title = "保存堡垒机账户"; Width = 660; Height = 680; MinWidth = 440; MinHeight = 330; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DropDownMotion.Attach(algorithm); DropDownMotion.Attach(digits);
        var account = previous ?? new(); name.Text = account.Name; portal.Text = account.PortalUrl; username.Text = account.Username; enabled.IsChecked = account.TotpEnabled;
        algorithm.SelectedIndex = account.Totp.Algorithm == "SHA256" ? 1 : account.Totp.Algorithm == "SHA512" ? 2 : 0; digits.SelectedIndex = account.Totp.Digits == 8 ? 1 : 0; period.Value = account.Totp.Period;
        password.PlaceholderText = previous?.HasPassword == true ? "留空保留系统原密码" : "仅在明确保存时写系统保管库"; seed.PlaceholderText = previous?.HasSeed == true ? "留空保留原key；Base32或otpauth URI" : "Base32 key 或 otpauth://totp URI";
        foreach (var item in new[] { (name, "账户名称"), (portal, "入口地址"), (username, "账户用户"), (password, "账户密码"), (seed, "验证器key") }) AutomationProperties.SetName(item.Item1, item.Item2);
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children = { UI.Label("账户名称"), name, UI.Label("入口地址（无凭据/查询/片段）"), portal, UI.Label("用户"), username, UI.Label("密码"), password, clearPassword, UI.Label("验证器key（仅输入，不回填已存key）"), seed, clearSeed, enabled, UI.Label("算法"), algorithm, UI.Label("位数"), digits, UI.Label("周期（秒）"), period } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right }; var cancel = UI.Button("取消", () => { if (!busy) Close(); }); actions.Children.Add(cancel);
        var save = UI.Button("保存", async () =>
        {
            if (busy) return;
            try
            {
                busy = true;
                var draft = account with { Name = name.Text ?? "", PortalUrl = portal.Text?.Trim() ?? "", Username = username.Text ?? "", TotpEnabled = enabled.IsChecked == true, Totp = new(algorithm.SelectedIndex == 1 ? "SHA256" : algorithm.SelectedIndex == 2 ? "SHA512" : "SHA1", digits.SelectedIndex == 1 ? 8 : 6, (int)(period.Value ?? 30)) };
                var saved = await repository.SaveAsync(previous, new(draft, password.Text ?? "", seed.Text ?? "", clearPassword.IsChecked == true, clearSeed.IsChecked == true), CancellationToken.None);
                password.Text = seed.Text = ""; busy = false; Close(saved);
            }
            catch (Exception e) { status.Text = "保存失败，当前输入保留：" + e.Message; }
            finally { busy = false; }
        }, true); actions.Children.Add(save);
        var footer = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children = { status, actions } }; var root = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(new ScrollViewer { Content = body }); Content = root;
        Closing += (_, e) => { if (busy) e.Cancel = true; }; Closed += (_, _) => password.Text = seed.Text = "";
    }
}
public sealed class TotpWindow : Window
{
    readonly TotpConfiguration configuration; readonly TextBlock code = new() { FontSize = 32, FontFamily = new FontFamily("Menlo,Consolas,monospace"), HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock remaining = UI.Label("", true); readonly CheckBox reveal = new() { Content = "显示验证码", IsChecked = false }; readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) }; readonly Func<DateTimeOffset> clock; bool closed;
    public TotpWindow(BastionAccount account, TotpConfiguration configuration, Func<DateTimeOffset>? clock = null)
    {
        this.configuration = configuration; this.clock = clock ?? (() => DateTimeOffset.UtcNow); Title = "本地TOTP验证器"; Width = 460; Height = 300; MinWidth = 360; MinHeight = 220; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(24), Children = { UI.Label(account.Name + " · " + account.Username), code, remaining, reveal, UI.Button("复制验证码", async () =>
        {
            try
            {
                if (closed) return;
                var value = Totp.Generate(configuration.Secret, configuration.Parameters, this.clock()).Code;
                if (Clipboard == null) { remaining.Text = "剪贴板不可用"; return; }
                var clipboard = Clipboard; await clipboard.SetTextAsync(value);
                _ = ClearClipboard(clipboard, value, TimeSpan.FromSeconds(configuration.Parameters.Period));
            }
            catch { remaining.Text = "复制失败，请重试"; }
        }), UI.Button("关闭", () => Close()) } }; Content = body;
        reveal.IsCheckedChanged += (_, _) => Update(); timer.Tick += (_, _) => { if (IsVisible) Update(); };
        Opened += (_, _) => { Update(); timer.Start(); }; Closed += (_, _) => { closed = true; timer.Stop(); CryptographicOperations.ZeroMemory(configuration.Secret); code.Text = ""; };
    }
    void Update() { if (closed) return; var current = Totp.Generate(configuration.Secret, configuration.Parameters, clock()); code.Text = reveal.IsChecked == true ? current.Code : new string('•', configuration.Parameters.Digits); remaining.Text = $"剩余{current.RemainingSeconds}秒 · 本地生成，请确认系统时间准确"; }
    public static async Task ClearClipboard(Avalonia.Input.Platform.IClipboard clipboard, string expected, TimeSpan delay)
    {
        try { await Task.Delay(delay); await Dispatcher.UIThread.InvokeAsync(async () => { if (await clipboard.TryGetTextAsync() == expected) await clipboard.SetTextAsync(""); }); } catch { }
    }
}
