using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class ConnectionWindow : Window
{
    readonly TextBox name = new(), group = new() { Text = "默认分组" }, host = new() { PlaceholderText = "主机名或 IP 地址" }, port = new() { Text = "22" }, user = new() { PlaceholderText = "登录用户" }, password = new() { PasswordChar = '●', PlaceholderText = "仅用于此次连接" };
    readonly TextBox agentFingerprint = new() { PlaceholderText = "可选：SHA256:... 指纹" }; readonly ComboBox jump = new() { HorizontalAlignment = HorizontalAlignment.Stretch }; readonly List<string> jumpIds = new() { "" }; readonly Func<IReadOnlyList<ConnectionProfile>> savedProfiles; readonly ConnectionCoordinator? coordinator;
    readonly TextBox key = new() { PlaceholderText = "本机私钥文件" }, phrase = new() { PasswordChar = '●', PlaceholderText = "加密私钥口令，仅本次" };
    readonly ComboBox protocol = new() { ItemsSource = new[] { "SSH", "SFTP", "FTP", "Telnet" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox authMode = new() { ItemsSource = new[] { "密码", "私钥", "交互验证", "本机agent" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox savePassword = new() { Content = "保存到系统凭据保管库" }, savePhrase = new() { Content = "保存私钥口令到系统保管库" };
    readonly TextBlock policy = UI.Label("", true), status = UI.Label("尚未测试连接", true);
    readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    readonly Grid main = new() { ColumnDefinitions = new ColumnDefinitions("150,1,*") };
    readonly StackPanel navigation = new() { Spacing = 10, Margin = new Thickness(12, 16) };
    readonly List<(Grid Row, Control Label, Control Value)> rows = new();
    readonly Button test, stop, save, cancel, close, more, choose;
    readonly TextBlock keyHint = UI.Label("", true); bool keyDetailsRequested; readonly Border basic, authentication, privateKey;
    readonly ConnectionProfile? original; readonly string draftId; readonly ProfileRepository repository; readonly KnownHosts knownHosts;
    CancellationTokenSource? testCancel; bool busy, saving;
    public ConnectionWindow(ConnectionProfile? p = null, ProfileRepository? repository = null, KnownHosts? knownHosts = null, Func<IReadOnlyList<ConnectionProfile>>? savedProfiles = null, ConnectionCoordinator? coordinator = null)
    {
        name.MaxLength = group.MaxLength = 256; host.MaxLength = user.MaxLength = 1024; port.MaxLength = 5; key.MaxLength = 4096; password.MaxLength = phrase.MaxLength = 32768;
        AutomationProperties.SetName(protocol, "连接协议"); AutomationProperties.SetName(authMode, "认证方式"); AutomationProperties.SetName(jump, "跳板连接");
        DropDownMotion.Attach(jump); DropDownMotion.Attach(protocol); DropDownMotion.Attach(authMode);
        original = p; draftId = p?.Id ?? Guid.NewGuid().ToString("N"); this.repository = repository ?? new(new ProfileStore(null), new UnavailableVault()); this.knownHosts = knownHosts ?? new(null); this.savedProfiles = savedProfiles ?? (() => Array.Empty<ConnectionProfile>()); this.coordinator = coordinator;
        Title = p == null ? "新建连接" : "编辑连接"; Width = 900; Height = 700; MinWidth = 440; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (p != null) { protocol.SelectedIndex = p.Protocol == "ftp" ? 2 : p.Protocol == "telnet" ? 3 : p.Protocol == "sftp" ? 1 : 0; name.Text = p.Name; group.Text = p.Group; host.Text = p.Host; port.Text = p.Port.ToString(); user.Text = p.User; key.Text = p.KeyPath; agentFingerprint.Text = p.AgentFingerprint; authMode.SelectedIndex = p.AuthMode == "key" ? 1 : p.AuthMode == "keyboard" ? 2 : p.AuthMode == "agent" ? 3 : 0; savePassword.IsChecked = p.HasPassword; savePhrase.IsChecked = p.HasPassphrase; if (p.HasPassword) password.PlaceholderText = "留空使用系统已保存密码"; if (p.HasPassphrase) phrase.PlaceholderText = "留空使用系统已保存口令"; }
        foreach (var (control, label) in new[] { (name, "连接名称"), (host, "主机地址"), (port, "端口"), (user, "登录用户"), (password, "登录密码"), (key, "私钥文件"), (phrase, "私钥口令") }) { AutomationProperties.SetName(control, label); control.GotFocus += (_, _) => Dispatcher.UIThread.Post(() => { if (control.IsVisible) control.BringIntoView(); }, DispatcherPriority.Loaded); }
        var root = new Grid { RowDefinitions = new RowDefinitions("52,*,Auto") }; root.Children.Add(new TextBlock { Text = Title, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
        close = UI.Button("×", () => { if (!saving) Close(null); }); close.Classes.Add("icon"); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 0, 16, 0); root.Children.Add(close);
        basic = Card(Row("协议", "SSH / SFTP 可共享连接。\nFTP 提供文件传输，Telnet 提供独立终端。", protocol), Row("名称", "易于辨认的连接名称", name), Row("分组", "连接管理中的分组", group), Row("地址 / 端口", "所选协议的目标主机与端口", Pair(host, port, "*,80")));
        authentication = Card(Row("验证方式", "服务器追加问题逐轮确认", authMode), Row("登录用户 / 密码", "保存新方式时清理旧凭据。\n选择方式本身不会改动保管库。", Pair(user, password, "*,*")), Row("密码保存", "默认仅用于本次；不会明文回退", savePassword), policy);
        choose = UI.Button("选择文件", async () => { var file = await new NativeFilePickers("选择私钥文件").UploadAsync(this); if (file != null) key.Text = file.Path; });
        var keyInput = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; keyInput.Children.Add(key); Grid.SetColumn(choose, 1); keyInput.Children.Add(choose);
        privateKey = Card(keyHint, Row("私钥文件", "本机普通文件，最多2MiB", keyInput), Row("私钥口令", "不自动使用登录密码或临时票据", phrase), Row("口令保存", "仅显式选择时写入系统保管库", savePhrase));
        var labels = new List<string> { "不使用跳板" }; foreach (var candidate in this.savedProfiles().Where(c => c.Id != draftId && c.Protocol is "ssh" or "sftp")) { jumpIds.Add(candidate.Id); labels.Add((candidate.Name.Length > 0 ? candidate.Name : candidate.Host) + " · " + candidate.Host); }
        jump.ItemsSource = labels; jump.SelectedIndex = Math.Max(0, jumpIds.IndexOf(p?.JumpId ?? ""));
        var agentRow = Row("agent钥匙", "仅连接已运行的本机agent，不导出私钥", agentFingerprint); var agentCard = Card(agentRow); agentCard.IsVisible = authMode.SelectedIndex == 3; authMode.SelectionChanged += (_, _) => agentCard.IsVisible = authMode.SelectedIndex == 3;
        var body = new StackPanel { Margin = new Thickness(16, 16, 24, 16), Children = { basic, authentication, privateKey, agentCard, Card(Row("跳板连接", "复用已有认证传输；不运行交互终端命令", jump), UI.Label("心跳与字号在偏好设置中配置。", true)) } }; scroll.Content = body;
        var sectionLinks = new List<Button>();
        void Section(string label, Action show)
        {
            Button? link = null; link = UI.Button(label, () => { foreach (var item in sectionLinks) item.Classes.Remove("active"); link!.Classes.Add("active"); show(); });
            link.Classes.Add("section-nav"); sectionLinks.Add(link); navigation.Children.Add(link);
        }
        Section("基本信息", () => basic.BringIntoView()); Section("身份认证", () => authentication.BringIntoView());
        Section("私钥设置", () => { keyDetailsRequested = true; UpdatePolicy(); Dispatcher.UIThread.Post(() => privateKey.BringIntoView(), DispatcherPriority.Loaded); });
        sectionLinks[0].Classes.Add("active");
        main.Children.Add(navigation); var line = new Border { Background = Brush.Parse("#3C3C40") }; Grid.SetColumn(line, 1); main.Children.Add(line); Grid.SetColumn(scroll, 2); main.Children.Add(scroll); Grid.SetRow(main, 1); root.Children.Add(main);
        test = UI.Button("✓  测试连接", Test); test.MinWidth = 120; stop = UI.Button("取消测试", () => testCancel?.Cancel()); stop.IsVisible = false; cancel = UI.Button("取消", () => Close(null)); save = UI.Button("保存并连接", () => Submit(true), true);
        more = UI.Button("⌄", () => { }); more.Classes.Add("icon"); ToolTip.SetTip(more, "更多保存方式：仅保存连接信息"); var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "仅保存连接信息", Command = new ActionCommand(() => Submit(false)) } } }; more.Click += (_, _) => menu.Open(more);
        var footer = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(20, 12, 20, 16) }; status.MaxHeight = 54; footer.Children.Add(status); var actions = new ActionBar { Margin = new Thickness(0, 12, 0, 0) }; actions.Children.Add(test); actions.Children.Add(stop); actions.Children.Add(cancel); actions.Children.Add(save); actions.Children.Add(more); Grid.SetRow(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        protocol.SelectionChanged += (_, _) => { if (protocol.SelectedIndex == 2) { authMode.SelectedIndex = 0; if (port.Text == "22") port.Text = "21"; } else if (protocol.SelectedIndex == 3) { authMode.SelectedIndex = 0; if (port.Text == "22") port.Text = "23"; } else if (port.Text is "21" or "23") port.Text = "22"; UpdatePolicy(); };
        authMode.SelectionChanged += (_, _) => UpdatePolicy(); savePassword.IsCheckedChanged += (_, _) => UpdatePolicy(); savePhrase.IsCheckedChanged += (_, _) => UpdatePolicy(); UpdatePolicy();
        SizeChanged += (_, _) => Responsive(); Opened += (_, _) => Responsive(); Closing += (_, e) => { if (saving) e.Cancel = true; }; Closed += (_, _) => { testCancel?.Cancel(); password.Text = ""; phrase.Text = ""; };
    }
    void UpdatePolicy()
    {
        bool isFtp = protocol.SelectedIndex == 2, isTelnet = protocol.SelectedIndex == 3, isAgent = authMode.SelectedIndex == 3, isKey = authMode.SelectedIndex == 1, editable = !busy;
        authMode.IsEnabled = editable && !isFtp && !isTelnet;
        privateKey.IsVisible = isKey && !isFtp && !isTelnet || keyDetailsRequested;
        password.IsEnabled = savePassword.IsEnabled = editable && !isKey && !isAgent;
        key.IsEnabled = phrase.IsEnabled = savePhrase.IsEnabled = choose.IsEnabled = editable && isKey && !isFtp;
        keyHint.Text = isFtp || isTelnet ? "当前协议使用账号密码，不使用SSH私钥。" : isKey ? "当前使用私钥认证。文件与口令仅在明确连接或保存时使用。" : "此处仅查看私钥设置；当前认证方式未改变。需要使用私钥时，请在“身份认证”的下拉框中明确选择。";
        policy.Text = isFtp ? "FTP使用账号密码；不自动重连。已有文件不主动覆盖。" : isTelnet ? "Telnet使用独立TCP终端，不复用SSH文件或监控通道。" : isAgent ? "只使用已运行的本机agent；保存此方式会移除旧登录密码与私钥口令。" : isKey ? "私钥口令单独保存；保存此方式会移除旧登录密码。" : savePassword.IsChecked == true ? "明确选择系统保存；失败会保留输入，不做明文回退。" : "密码仅用于测试或本次连接；仅保存信息会丢弃输入。取消系统保存会移除旧密码。";
    }
    Grid Row(string text, string note, Control value)
    {
        var label = new StackPanel { Spacing = 5, Children = { new TextBlock { Text = text, FontWeight = FontWeight.SemiBold }, UI.Label(note, true) } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 24, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(row, "连接字段 " + text); row.Children.Add(label); Grid.SetColumn(value, 1); row.Children.Add(value); rows.Add((row, label, value)); return row;
    }
    static Border Card(params Control[] content) => new() { Classes = { "card" }, Child = new StackPanel { Spacing = 8 }.Also(s => { foreach (var c in content) s.Children.Add(c); }) };
    static Grid Pair(Control first, Control second, string columns) { var p = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 8 }; p.Children.Add(first); Grid.SetColumn(second, 1); p.Children.Add(second); return p; }
    void Responsive() { bool narrow = Bounds.Width < 700; main.ColumnDefinitions = new ColumnDefinitions(narrow ? "0,0,*" : "150,1,*"); navigation.IsVisible = !narrow; foreach (var (row, label, value) in rows) { row.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "220,*"); row.ColumnSpacing = narrow ? 0 : 24; row.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto"); Grid.SetColumn(value, narrow ? 0 : 1); Grid.SetRow(value, narrow ? 1 : 0); value.Margin = new Thickness(0, narrow ? 10 : 0, 0, 0); } }
    ConnectionProfile Snapshot() { if (protocol.SelectedIndex is 2 or 3 && (jump.SelectedIndex > 0 || authMode.SelectedIndex != 0)) throw new IOException("FTP和Telnet使用账号密码，不能绕过已有SSH跳板；请先明确取消跳板"); if (string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(user.Text) || !int.TryParse(port.Text, out var n) || n is < 1 or > 65535) throw new IOException("请填写主机、用户和有效端口1–65535"); string mode = authMode.SelectedIndex == 1 ? "key" : authMode.SelectedIndex == 2 ? "keyboard" : authMode.SelectedIndex == 3 ? "agent" : "password"; if (mode == "key" && string.IsNullOrWhiteSpace(key.Text)) throw new IOException("请选择私钥文件"); return new(name.Text ?? "", group.Text ?? "", host.Text.Trim(), n, user.Text.Trim()) { Id = draftId, Revision = original?.Revision ?? "0", Protocol = protocol.SelectedIndex == 2 ? "ftp" : protocol.SelectedIndex == 3 ? "telnet" : protocol.SelectedIndex == 1 ? "sftp" : "ssh", AuthMode = mode, KeyPath = mode == "key" ? key.Text ?? "" : "", AgentFingerprint = mode == "agent" ? agentFingerprint.Text?.Trim() ?? "" : "", JumpId = jump.SelectedIndex >= 0 && jump.SelectedIndex < jumpIds.Count ? jumpIds[jump.SelectedIndex] : "" }; }
    void Busy(bool value, bool isSaving) { busy = value; saving = value && isSaving; foreach (var c in new Control[] { protocol, name, group, host, port, user, password, key, phrase, authMode, agentFingerprint, jump, savePassword, savePhrase, choose, test, save, more }) c.IsEnabled = !value; cancel.IsEnabled = close.IsEnabled = !saving; stop.IsVisible = value && !isSaving; stop.IsEnabled = stop.IsVisible; UpdatePolicy(); }
    async void Submit(bool connect)
    {
        if (busy) return;
        try
        {
            var p = Snapshot(); ConnectionCoordinator.ResolveChain(p, savedProfiles()); if (!connect && ((!string.IsNullOrEmpty(password.Text) && savePassword.IsChecked != true) || (!string.IsNullOrEmpty(phrase.Text) && savePhrase.IsChecked != true)) && !await UI.Confirm(this, "仅保存连接信息？", "未选择系统保存的密码或口令将被丢弃；之后连接会再次询问。")) return;
            Busy(true, true); var committed = await repository.SaveAsync(original, new(p, password.Text ?? "", phrase.Text ?? "", savePassword.IsChecked == true, savePhrase.IsChecked == true), CancellationToken.None); saving = false;
            Close(new ConnectionRequest(committed, password.Text ?? "", true, connect, phrase.Text ?? ""));
        }
        catch (Exception e) { status.Text = "保存失败，当前输入仍保留：" + e.Message; }
        finally { Busy(false, false); }
    }
    async void Test()
    {
        if (busy) return; try
        {
            var p = Snapshot(); Busy(true, false); testCancel = new CancellationTokenSource(TimeSpan.FromSeconds(45)); status.Text = "只测试认证；不开shell、不写远程文件、不保存凭据或主机指纹";
            string pw = password.Text ?? "", passphrase = phrase.Text ?? "";
            if ((p.AuthMode is "password" or "keyboard") && pw.Length == 0 && original?.HasPassword == true && !ProfileRepository.CanReusePassword(original, p)) throw new IOException("协议或账号地址已变化，请输入本次密码；旧密码不会自动转用");
            if ((p.AuthMode is "password" or "keyboard") && pw.Length == 0 && original?.HasPassword == true) pw = await repository.PasswordAsync(original, testCancel.Token) ?? throw new IOException("系统密码不存在，请输入本次密码");
            if (p.AuthMode == "key" && passphrase.Length == 0 && original?.HasPassphrase == true && !ProfileRepository.CanReusePassphrase(original, p)) throw new IOException("私钥或协议已变化，请重新输入本次口令");
            if (p.AuthMode == "key" && passphrase.Length == 0 && original?.HasPassphrase == true) passphrase = await repository.PassphraseAsync(original, testCancel.Token) ?? throw new IOException("系统私钥口令不存在，请输入本次口令");
            if (p.Protocol == "ftp") { if (p.JumpId.Length > 0) throw new IOException("FTP不支持SSH跳板，未绕过配置直连"); using var ftp = await FtpFileSession.ConnectAsync(p.Host, p.Port, p.User, pw, testCancel.Token, new() { KeepAliveEnabled = false }); status.Text = "FTP认证成功；未读取列表或探测写权限"; return; }
            if (p.Protocol == "telnet") { if (p.JumpId.Length > 0) throw new IOException("Telnet不支持SSH跳板，未绕过配置直连"); using var telnet = await TelnetPtyConnection.ConnectAsync(p.Host, p.Port, testCancel.Token); status.Text = "Telnet连接成功；未启动持久工作区"; return; }
            using var connection = coordinator != null ? await coordinator.ConnectAsync(p, pw, passphrase, false, testCancel.Token, rememberHostKeys: false) : await SharedSshSession.ConnectAsync(p.Host, p.Port, p.User, pw, (fp, ct) => UI.VerifyFingerprint(this, knownHosts, p.Host, p.Port, fp, false, ct), testCancel.Token, false, (q, ct) => Dispatcher.UIThread.InvokeAsync(() => UI.AskCredentials(this, q, ct)), p.AuthMode, p.KeyPath, passphrase); status.Text = "SSH认证成功；未探测文件写权限";
        }
        catch (Exception e) { status.Text = e is OperationCanceledException ? "测试已取消或超时" : e.Message; }
        finally { testCancel?.Dispose(); testCancel = null; Busy(false, false); }
    }
}
public static class Construction { public static T Also<T>(this T value, Action<T> action) { action(value); return value; } }
public sealed class ActionCommand(Action action) : System.Windows.Input.ICommand { public bool CanExecute(object? p) => true; public void Execute(object? p) => action(); public event EventHandler? CanExecuteChanged { add { } remove { } } }
