using System.Reflection;
using Avalonia;
using Forterm.Core;
using System.Threading.Channels;
using System.Diagnostics;
namespace Forterm.Desktop;

internal static class Program
{
    internal static readonly Channel<LaunchPlan> LaunchInbox = Channel.CreateBounded<LaunchPlan>(128);
    static readonly LaunchGate Gate = new(); static readonly Stopwatch Clock = Stopwatch.StartNew();
    static bool Accept(string[] args, bool initial = false) { var p = LaunchPlan.Parse(args); if (!Gate.Accept(p, Clock.Elapsed)) { if (p.Connection != null) p.Connection.Password = ""; return true; } if (initial) p.NewWindow = false; if (LaunchInbox.Writer.TryWrite(p)) return true; if (p.Connection != null) p.Connection.Password = ""; return false; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--runtime-check" }))
        {
            using var bitmap = new SkiaSharp.SKBitmap(32, 32); using var buffer = new HarfBuzzSharp.Buffer(); buffer.AddUtf16("Forterm中文");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Version = "0.3.0-alpha.36", Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Skia = bitmap.Width, HarfBuzz = buffer.Length, TerminalEnginePatch = typeof(XTerm.Terminal).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "FortermPatchId").Value, TerminalControlPatch = typeof(Iciclecreek.Terminal.TerminalControl).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "FortermPatchId").Value, VisibleWindowsCreated = 0, CredentialApi = OsCredentialVault.ProbeNativeSymbols() })); return;
        }
        var initial = LaunchPlan.Parse(args);
        if (initial.Diagnose) { Console.WriteLine(initial.ToString()); if (initial.Connection != null) initial.Connection.Password = ""; Array.Clear(args); return; }
        if (initial.Connection != null) initial.Connection.Password = "";
        try
        {
            var result = InstanceBroker.StartAsync(InstanceBroker.ForDataDirectory(ProfileStore.DefaultDirectory), args, envelope => Accept(envelope), CancellationToken.None).GetAwaiter().GetResult();
            if (result.Forwarded) return;
            using var broker = result.Primary!; Accept(args, true); Array.Clear(args);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        finally { Array.Clear(args); Gate.Dispose(); }

    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
