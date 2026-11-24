using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Forterm.Core;
namespace Forterm.Desktop;
public sealed class MetadataWindow : Window
{
    readonly TextBlock status = UI.Label("导入仅追加新标识，原连接和系统秘密保留。连接与账户分别确认。", true);
    bool busy;
    public MetadataWindow(ProfileStore store, ProfileRepository profiles, AccountRepository accounts, Action reload, IFilePickers? pickers = null)
    {
        Title = "无秘密配置导入与导出"; Width = 640; Height = 480; MinWidth = 420; MinHeight = 300; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        pickers ??= new NativeFilePickers("导入无秘密配置", "导出无秘密配置");
        async void Run(Func<Task> work)
        {
            if (busy) return; busy = true;
            try { await work(); } catch (Exception e) { status.Text = "操作未完成：" + e.Message; } finally { busy = false; }
        }
        async Task Export(string name, string text)
        {
            string? path = await pickers.DownloadAsync(this, name); if (path == null) { status.Text = "已取消导出"; return; }
            bool saved = await LocalTextFiles.WriteAsync(path, text, (target, ct) => UI.Confirm(this, "替换配置导出？", target.Path + "\n不含密码、密钥或临时票据。", ct), CancellationToken.None); status.Text = saved ? "无秘密配置已导出" : "已取消导出";
        }
        async Task ImportProfiles()
        {
            var file = await pickers.UploadAsync(this); if (file == null) { status.Text = "已取消选择"; return; }
            var parsed = MetadataExchange.ReadProfiles(await MetadataExchange.ReadFileAsync(file.Path, CancellationToken.None));
            if (!await UI.Confirm(this, "追加连接配置？", $"共{parsed.Length}个连接，将生成全新标识、重映射跳板并清除密码存在标记。\n不会导入指纹信任、读取私钥或连接服务器。\n" + string.Join("\n", parsed.Take(10).Select(p => p.Name + " · " + p.Protocol + " · " + p.Host + ":" + p.Port)))) { status.Text = "已取消导入"; return; }
            await profiles.ImportMetadataAsync(parsed, CancellationToken.None); reload(); status.Text = $"已追加{parsed.Length}个连接；请在连接前检查私钥路径与账号";
        }
        async Task ImportAccounts()
        {
            var file = await pickers.UploadAsync(this); if (file == null) { status.Text = "已取消选择"; return; }
            var parsed = MetadataExchange.ReadAccounts(await MetadataExchange.ReadFileAsync(file.Path, CancellationToken.None));
            if (!await UI.Confirm(this, "追加账户配置？", $"共{parsed.Length}个账户，将生成新标识，清除秘密存在标记并停用验证器。\n不写系统秘密，不访问门户。\n" + string.Join("\n", parsed.Take(10).Select(a => a.Name + " · " + a.Username)))) { status.Text = "已取消导入"; return; }
            await accounts.ImportMetadataAsync(parsed, CancellationToken.None); status.Text = $"已追加{parsed.Length}个账户；验证器需重新明确保存key";
        }
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(20), Children = {
            UI.Label("支持本版无秘密配置；旧Go v1导出文件可由用户明确选择导入。不会自动读取旧版本配置。", true),
            UI.Button("导出连接配置", () => Run(() => Export("forterm-profiles.json", MetadataExchange.ExportProfiles(store.Load())))),
            UI.Button("导入连接配置", () => Run(ImportProfiles)),
            UI.Button("导出账户配置", () => Run(() => Export("forterm-accounts.json", MetadataExchange.ExportAccounts(accounts.Load())))),
            UI.Button("导入账户配置", () => Run(ImportAccounts)), status } };
        var root = new DockPanel(); var close = UI.Button("关闭", () => { if (!busy) Close(); }); close.Margin = new Thickness(20); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close); root.Children.Add(new ScrollViewer { Content = body }); Content = root; Closing += (_, e) => e.Cancel = busy;
    }
}
