using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace Forterm.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            var main = new MainWindow(); desktop.MainWindow = main;
            var lifetime = new CancellationTokenSource(); desktop.Exit += (_, _) => lifetime.Cancel();
            main.Loaded += async (_, _) =>
            {
                try
                {
                    await foreach (var plan in Program.LaunchInbox.Reader.ReadAllAsync(lifetime.Token))
                    { var target = desktop.Windows.OfType<MainWindow>().LastOrDefault(w => w.IsVisible); if (plan.NewWindow || target == null) { target = new MainWindow(); target.Show(); } await target.AcceptLaunch(plan); }
                }
                catch (OperationCanceledException) { }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
