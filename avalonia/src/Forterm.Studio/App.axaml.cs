using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Views;

namespace Forterm.Studio;

public sealed partial class App : Application
{
    string? startupNotice;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
            Forterm.Studio.Services.Appearance.Apply(Forterm.Studio.Services.Appearance.Load(StudioPaths.DataDirectory, out string? settingsProblem));
            Forterm.Studio.Services.Motion.Follow(this);
            var knownHosts = new KnownHosts(StudioPaths.DataDirectory);
            var credentials = new CredentialStore(VaultFactory.ForCurrentPlatform(StudioPaths.DataDirectory));
            LibraryStore library;
            // Any failure here (damaged or hand-edited file) must not stop the app: controller
            // wake-ups still need to work.
            try { library = new LibraryStore(StudioPaths.DataDirectory); }
            catch (Exception e)
            {
                // Never overwrite an unreadable library: run with an in-memory one and say so.
                library = new LibraryStore(null);
                startupNotice = "连接库文件无法读取，本次使用临时空连接库，原文件未改动：" + e.Message;
            }
            AccountStore accounts;
            try { accounts = new AccountStore(StudioPaths.DataDirectory); }
            catch (Exception e)
            {
                accounts = new AccountStore(null);
                startupNotice ??= "账户文件无法读取，本次使用临时空账户列表，原文件未改动：" + e.Message;
            }
            QuickCommandStore commands;
            try { commands = new QuickCommandStore(StudioPaths.DataDirectory); }
            catch (Exception e)
            {
                commands = new QuickCommandStore(null);
                startupNotice ??= "快速命令文件无法读取，本次使用临时空列表，原文件未改动：" + e.Message;
            }
            if (settingsProblem != null) startupNotice ??= "设置文件无法读取，本次使用默认设置；修改设置时原文件会改名保留：" + settingsProblem;
            var main = MainWindow.Create(knownHosts, library, credentials, accounts, commands);
            if (startupNotice != null) main.ViewModel.Notice = startupNotice;
            desktop.MainWindow = main;
            var lifetime = new CancellationTokenSource();
            desktop.Exit += (_, _) => lifetime.Cancel();
            main.Opened += async (_, _) =>
            {
                // Controller launches (the first argv and later forwarded ones) arrive here.
                // "-newwin" opens a separate window; otherwise the most recent window takes it.
                // Each launch runs on its own: one waiting on a password, host-key or slow bastion
                // never holds back the next (already acknowledged) one, and a failure stays local.
                try
                {
                    await foreach (var plan in Program.LaunchInbox.Reader.ReadAllAsync(lifetime.Token))
                    {
                        try
                        {
                            var target = desktop.Windows.OfType<MainWindow>().LastOrDefault(w => w.IsVisible && !w.IsClosing);
                            if (plan.NewWindow || target == null) { target = MainWindow.Create(knownHosts, library, credentials, accounts, commands); target.Show(); }
                            target.Activate();
                            _ = AcceptAsync(target, plan);
                        }
                        catch (Exception e)
                        {
                            if (plan.Connection != null) plan.Connection.Password = "";
                            main.ViewModel.Notice = "唤醒处理失败：" + Forterm.Studio.ViewModels.WorkspaceViewModel.Short(e.Message);
                        }
                    }
                }
                catch (OperationCanceledException) { }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    static async Task AcceptAsync(MainWindow target, Forterm.Core.LaunchPlan plan)
    {
        try { await target.AcceptLaunchAsync(plan); }
        catch (Exception e)
        {
            if (plan.Connection != null) plan.Connection.Password = "";
            target.ViewModel.Notice = "唤醒处理失败：" + Forterm.Studio.ViewModels.WorkspaceViewModel.Short(e.Message);
        }
    }
}
